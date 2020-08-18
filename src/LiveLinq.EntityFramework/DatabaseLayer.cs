using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AutoMapper;
using ComposableCollections;
using ComposableCollections.Dictionary;
using Microsoft.EntityFrameworkCore;
using SimpleMonads;

namespace LiveLinq.EntityFramework
{
    public static class DatabaseLayer
    {
        public static DatabaseLayer<TDbContext> Create<TDbContext>(Func<TDbContext> createDbContext) where TDbContext : DbContext
        {
            return new DatabaseLayer<TDbContext>(createDbContext);
        }
    }
    
    public class DatabaseLayer<TDbContext> where TDbContext : DbContext
    {
        private readonly Func<TDbContext> _create;
        private ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private List<Func<IEnumerable<Action<TDbContext>>>> _getMutationses = new List<Func<IEnumerable<Action<TDbContext>>>>();
        private IComposableDictionary<Type, object> _composableDictionaries = new ComposableDictionary<Type, object>();
        
        public DatabaseLayer(Func<TDbContext> create)
        {
            _create = create;
        }

        private IComposableDictionary<TKey, TValue> GetComposableDictionary<TKey, TValue>()
        {
            var result = (IComposableDictionary<TKey, TValue>) _composableDictionaries[typeof(IKeyValue<TKey, TValue>)];
            return result;
        }

        private void OnFinishTransaction()
        {
            
        }
        
        public void MutateAtomically<TKey, TValue>(Action<IComposableDictionary<TKey, TValue>> action)
        {
            _lock.EnterReadLock();
            try
            {
                var dict1 = GetComposableDictionary<TKey, TValue>();
                action(dict1);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
        
        public void MutateAtomically<TKey1, TValue1, TKey2, TValue2>(Action<IComposableDictionary<TKey1, TValue1>, IComposableDictionary<TKey2, TValue2>> action)
        {
            _lock.EnterReadLock();
            try
            {
                var dict1 = GetComposableDictionary<TKey1, TValue1>();
                var dict2 = GetComposableDictionary<TKey2, TValue2>();
                action(dict1, dict2);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
        
        public IComposableDictionary<TId, TDbDto> WithAggregateRoot<TId, TDbDto>(Func<TDbContext, DbSet<TDbDto>> dbSet,
            Func<TDbDto, TId> id, Func<DbSet<TDbDto>, TId, TDbDto> find) where TDbDto : class
        {
            _lock.EnterWriteLock();
            try
            {
                var result = new AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext>(dbSet, _create, id, find)
                    .WithMinimalCaching();
            
                var mapperConfig = new MapperConfiguration(cfg =>
                {
                    cfg.CreateMap<TDbDto, TDbDto>()
                        .PreserveReferences();
                });

                var mapper = mapperConfig.CreateMapper();
                
                _composableDictionaries[typeof(IKeyValue<TId, TDbDto>)] = result;
                _getMutationses.Add(() => result.GetMutations(true).Select(mutation =>
                {
                    Action<TDbContext> action = context => Execute(context, mutation, dbSet, find, mapper, out var mutationResult);
                    return action;
                }));
                
                return result;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private void Execute<TId, TDbDto>(TDbContext context, DictionaryMutation<TId, TDbDto> mutation, Func<TDbContext, DbSet<TDbDto>> getDbSet, Func<DbSet<TDbDto>, TId, TDbDto> find, IMapper mapper, out DictionaryMutationResult<TId, TDbDto> result) where TDbDto : class
        {
            if (mutation.Type == DictionaryMutationType.Add)
            {
                var preExistingValue = find(getDbSet(context), mutation.Key);
                if (preExistingValue != null)
                {
                    context.Entry(preExistingValue).State = EntityState.Detached;
                    throw new InvalidOperationException("Cannot add an item when an item with that key already exists");
                }

                var newValue = mutation.ValueIfAdding.Value();
                getDbSet(context).Add(newValue);
                result = (DictionaryMutationResult<TId, TDbDto>.CreateAdd(mutation.Key, true, Maybe<TDbDto>.Nothing(), newValue.ToMaybe()));
            }
            else if (mutation.Type == DictionaryMutationType.TryAdd)
            {
                var preExistingValue = find(getDbSet(context), mutation.Key);
                if (preExistingValue != null)
                {
                    context.Entry(preExistingValue).State = EntityState.Detached;
                    result = (DictionaryMutationResult<TId, TDbDto>.CreateAdd(mutation.Key, false, preExistingValue.ToMaybe(), Maybe<TDbDto>.Nothing()));
                }
                else
                {
                    var newValue = mutation.ValueIfAdding.Value();
                    getDbSet(context).Add(newValue);
                    result = (DictionaryMutationResult<TId, TDbDto>.CreateAdd(mutation.Key, true, Maybe<TDbDto>.Nothing(), newValue.ToMaybe()));
                }
            }
            else if (mutation.Type == DictionaryMutationType.Update)
            {
                var preExistingValue = find(getDbSet(context), mutation.Key);
                if (preExistingValue != null)
                {
                    var oldPreExistingValue = mapper.Map<TDbDto, TDbDto>(preExistingValue);

                    var updatedValue = mutation.ValueIfUpdating.Value(preExistingValue);
                    mapper.Map(updatedValue, preExistingValue);
                    getDbSet(context).Update(preExistingValue);
                    result = (DictionaryMutationResult<TId, TDbDto>.CreateUpdate(mutation.Key, true, oldPreExistingValue.ToMaybe(), preExistingValue.ToMaybe()));
                }
                else
                {
                    throw new InvalidOperationException("Cannot update an item when no item with that key already exists");
                }
            }
            else if (mutation.Type == DictionaryMutationType.TryUpdate)
            {
                var preExistingValue = find(getDbSet(context), mutation.Key);
                if (preExistingValue != null)
                {
                    var oldPreExistingValue = mapper.Map<TDbDto, TDbDto>(preExistingValue);
                    var updatedValue = mutation.ValueIfUpdating.Value(preExistingValue);
                    mapper.Map(updatedValue, preExistingValue);
                    getDbSet(context).Update(preExistingValue);
                    result = (DictionaryMutationResult<TId, TDbDto>.CreateUpdate(mutation.Key, true, oldPreExistingValue.ToMaybe(), preExistingValue.ToMaybe()));
                }
                else
                {
                    result = (DictionaryMutationResult<TId, TDbDto>.CreateUpdate(mutation.Key, true, Maybe<TDbDto>.Nothing(), Maybe<TDbDto>.Nothing()));
                }
            }
            else if (mutation.Type == DictionaryMutationType.Remove)
            {
                var preExistingValue = find(getDbSet(context), mutation.Key);
                if (preExistingValue != null)
                {
                    context.Entry(preExistingValue).State = EntityState.Detached;
                    getDbSet(context).Remove(preExistingValue);
                    result = (DictionaryMutationResult<TId, TDbDto>.CreateRemove(mutation.Key, preExistingValue.ToMaybe()));
                }
                else
                {
                    throw new InvalidOperationException("Cannot remove an item when no item with that key already exists");
                }
            }
            else if (mutation.Type == DictionaryMutationType.TryRemove)
            {
                var preExistingValue = find(getDbSet(context), mutation.Key);
                if (preExistingValue != null)
                {
                    context.Entry(preExistingValue).State = EntityState.Detached;
                    getDbSet(context).Remove(preExistingValue);
                    result = (DictionaryMutationResult<TId, TDbDto>.CreateRemove(mutation.Key, preExistingValue.ToMaybe()));
                }
                else
                {
                    result = (DictionaryMutationResult<TId, TDbDto>.CreateRemove(mutation.Key, Maybe<TDbDto>.Nothing()));
                }
            }
            else if (mutation.Type == DictionaryMutationType.AddOrUpdate)
            {
                var preExistingValue = find(getDbSet(context), mutation.Key);
                if (preExistingValue != null)
                {
                    var oldPreExistingValue = mapper.Map<TDbDto, TDbDto>(preExistingValue);
                    var updatedValue = mutation.ValueIfUpdating.Value(preExistingValue);
                    mapper.Map(updatedValue, preExistingValue);
                    getDbSet(context).Update(preExistingValue);
                    result = (DictionaryMutationResult<TId, TDbDto>.CreateAddOrUpdate(mutation.Key, DictionaryItemAddOrUpdateResult.Update, oldPreExistingValue.ToMaybe(), preExistingValue));
                }
                else
                {
                    var updatedValue = mutation.ValueIfAdding.Value();
                    getDbSet(context).Add(updatedValue);
                    result = (DictionaryMutationResult<TId, TDbDto>.CreateAddOrUpdate(mutation.Key, DictionaryItemAddOrUpdateResult.Update, Maybe<TDbDto>.Nothing(), updatedValue));
                }
            }
            else
            {
                throw new InvalidOperationException($"Unknown mutation type {mutation.Type}");
            }
        }
        
        public IComposableDictionary<TId, TDbDto> WithAggregateRoot<TId, TDbDto>(Func<TDbContext, DbSet<TDbDto>> dbSet,
            Func<TDbDto, TId> id) where TDbDto : class
        {
            return new AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext>(dbSet, _create, id);
        }
        
        public void FlushCache()
        {
            _lock.EnterWriteLock();
            try
            {
                using (var context = _create())
                {
                    using (var transaction = context.Database.BeginTransaction())
                    {
                        try
                        {
                            foreach (var mutation in _getMutationses.SelectMany(getMutations => getMutations()))
                            {
                                mutation(context);
                            }
                            transaction.Commit();
                        }
                        catch(Exception ex)
                        {
                            transaction.Rollback();
                            
                            throw ex;
                        }
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }
}