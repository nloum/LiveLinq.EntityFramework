using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AutoMapper;
using ComposableCollections.Dictionary;
using Microsoft.EntityFrameworkCore;
using SimpleMonads;

namespace LiveLinq.EntityFramework
{
    public static class DatabaseLayer
    {
        public static DatabaseLayer<TDbContext> Create<TDbContext>(Func<TDbContext> createDbContext, Action<TDbContext> migrate = null) where TDbContext : DbContext
        {
            return new DatabaseLayer<TDbContext>(createDbContext, migrate);
        }
    }

    public class DatabaseLayer<TDbContext> where TDbContext : DbContext
    {
        private readonly Func<TDbContext> _create;
        private ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private List<Func<IEnumerable<Action<TDbContext>>>> _getMutationses = new List<Func<IEnumerable<Action<TDbContext>>>>();
        private IComposableDictionary<Type, object> _composableDictionaries = new ComposableDictionary<Type, object>();
        private bool _hasMigratedYet = false;
        private readonly List<Action<IMapperConfigurationExpression>> _mapperConfigs = new List<Action<IMapperConfigurationExpression>>();
        private IMapper _mapper;

        public DatabaseLayer(Func<TDbContext> create, Action<TDbContext> migrate = null)
        {
            if (migrate == null)
            {
                _create = create;
                _hasMigratedYet = true;
            }
            else
            {
                _hasMigratedYet = false;
                _create = () =>
                {
                    if (!_hasMigratedYet)
                    {
                        using (var context = create())
                        {
                            migrate(context);
                        }

                        _hasMigratedYet = true;
                    }

                    return create();
                };
            }
        }

        public ITransactionalCollection<IDisposableReadOnlyDictionary<TId, TDbDto>, IDisposableDictionary<TId, TDbDto>> WithAggregateRoot<TId, TDbDto>(Func<TDbContext, DbSet<TDbDto>> dbSet,
            Func<TDbDto, TId> dbDtoId, Func<DbSet<TDbDto>, TId, TDbDto> find = null) where TDbDto : class, new()
        {
            if (find == null)
            {
                find = (theDbSet, theId) => theDbSet.Find(theId);
            }

            _lock.EnterWriteLock();
            try
            {
                _mapperConfigs.Add(cfg =>
                {
                    cfg.CreateMap<TDbDto, TDbDto>()
                        .PreserveReferences();
                });

                var result = new AnonymousTransactionalCollection<IDisposableReadOnlyDictionary<TId, TDbDto>, IDisposableDictionary<TId, TDbDto>>(() =>
                {
                    var dbContext = _create();
                    var theDbSet = dbSet(dbContext);
                    var efCoreDict = new AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext>(dbContext, theDbSet,
                        dbDtoId, find);
                    return new DisposableDictionaryDecorator<TId, TDbDto>(efCoreDict, dbContext);
                }, () =>
                {
                    var dbContext = _create();
                    var theDbSet = dbSet(dbContext);
                    var efCoreDict = new AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext>(dbContext, theDbSet,
                        dbDtoId, find);
                    return new DisposableDictionaryDecorator<TId, TDbDto>(efCoreDict, dbContext);
                });
                
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

                            context.SaveChanges();
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