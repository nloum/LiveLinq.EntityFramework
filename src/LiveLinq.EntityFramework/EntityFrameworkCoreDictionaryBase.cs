using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AutoMapper;
using ComposableCollections.Dictionary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MoreCollections;
using SimpleMonads;

namespace LiveLinq.EntityFramework
{
    public abstract class EntityFrameworkCoreDictionaryBase<TId, TDbDto, TDbContext> : DictionaryBase<TId, TDbDto> where TDbDto : class where TDbContext : DbContext
    {
        private bool _hasMigratedYet;

        private readonly IMapper _mapper;

        protected abstract DbSet<TDbDto> GetDbSet(TDbContext context);
        protected abstract TDbContext CreateDbContext();
        protected abstract TId GetId(TDbDto dbDto);
        protected abstract void Migrate(DatabaseFacade database);

        protected EntityFrameworkCoreDictionaryBase(bool migrate)
        {
            _hasMigratedYet = !migrate;
            
            var mapperConfig = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<TDbDto, TDbDto>();
            });

            _mapper = mapperConfig.CreateMapper();
        }

        private TDbContext PrivateCreateDbContext()
        {
            if (!_hasMigratedYet)
            {
                using (var context = CreateDbContext())
                {
                    Migrate(context.Database);
                }
            }

            var result = CreateDbContext();
            result.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            return result;
        }

        public override bool TryGetValue(TId key, out TDbDto value)
        {
            using (var context = PrivateCreateDbContext())
            {
                value = GetDbSet(context).Find(key);
                if (value != null)
                {
                    context.Entry(value).State = EntityState.Detached;
                }
                return value != null;
            }
        }

        public override IEnumerator<IKeyValue<TId, TDbDto>> GetEnumerator()
        {
            using (var context = PrivateCreateDbContext())
            {
                return GetDbSet(context).AsNoTracking().AsEnumerable().Select(value => new KeyValue<TId, TDbDto>(GetId(value), value)).ToImmutableList().GetEnumerator();
            }
        }

        public override int Count
        {
            get
            {
                using (var context = PrivateCreateDbContext())
                {
                    return GetDbSet(context).Count();
                }
            }
        }
        public override IEqualityComparer<TId> Comparer { get; } = EqualityComparer<TId>.Default;

        public override IEnumerable<TId> Keys => this.Select(kvp => kvp.Key);

        public override IEnumerable<TDbDto> Values => this.Select(kvp => kvp.Value);
        public override void Mutate(IEnumerable<DictionaryMutation<TId, TDbDto>> mutations, out IReadOnlyList<DictionaryMutationResult<TId, TDbDto>> results)
        {
            var finalResults = new List<DictionaryMutationResult<TId, TDbDto>>();
            results = finalResults;

            using (var context = PrivateCreateDbContext())
            {
                using (var transaction = context.Database.BeginTransaction())
                {
                    try
                    {
                        foreach (var mutation in mutations)
                        {
                            if (mutation.Type == DictionaryMutationType.Add)
                            {
                                var preExistingValue = GetDbSet(context).Find(mutation.Key);
                                if (preExistingValue != null)
                                {
                                    context.Entry(preExistingValue).State = EntityState.Detached;
                                    throw new InvalidOperationException("Cannot add an item when an item with that key already exists");
                                }

                                var newValue = mutation.ValueIfAdding.Value();
                                GetDbSet(context).Add(newValue);
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateAdd(mutation.Key, true, Maybe<TDbDto>.Nothing(), newValue.ToMaybe()));
                            }
                            else if (mutation.Type == DictionaryMutationType.TryAdd)
                            {
                                var preExistingValue = GetDbSet(context).Find(mutation.Key);
                                if (preExistingValue != null)
                                {
                                    context.Entry(preExistingValue).State = EntityState.Detached;
                                    finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateAdd(mutation.Key, false, preExistingValue.ToMaybe(), Maybe<TDbDto>.Nothing()));
                                }
                                else
                                {
                                    var newValue = mutation.ValueIfAdding.Value();
                                    GetDbSet(context).Add(newValue);
                                    finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateAdd(mutation.Key, true, Maybe<TDbDto>.Nothing(), newValue.ToMaybe()));
                                }
                            }
                            else if (mutation.Type == DictionaryMutationType.Update)
                            {
                                var preExistingValue = GetDbSet(context).Find(mutation.Key);
                                if (preExistingValue != null)
                                {
                                    var oldPreExistingValue = _mapper.Map<TDbDto, TDbDto>(preExistingValue);

                                    var updatedValue = mutation.ValueIfUpdating.Value(preExistingValue);
                                    _mapper.Map(updatedValue, preExistingValue);
                                    GetDbSet(context).Update(preExistingValue);
                                    finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateUpdate(mutation.Key, true, oldPreExistingValue.ToMaybe(), preExistingValue.ToMaybe()));
                                }
                                else
                                {
                                    throw new InvalidOperationException("Cannot update an item when no item with that key already exists");
                                }
                            }
                            else if (mutation.Type == DictionaryMutationType.TryUpdate)
                            {
                                var preExistingValue = GetDbSet(context).Find(mutation.Key);
                                if (preExistingValue != null)
                                {
                                    var oldPreExistingValue = _mapper.Map<TDbDto, TDbDto>(preExistingValue);
                                    var updatedValue = mutation.ValueIfUpdating.Value(preExistingValue);
                                    _mapper.Map(updatedValue, preExistingValue);
                                    GetDbSet(context).Update(preExistingValue);
                                    finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateUpdate(mutation.Key, true, oldPreExistingValue.ToMaybe(), preExistingValue.ToMaybe()));
                                }
                                else
                                {
                                    finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateUpdate(mutation.Key, true, Maybe<TDbDto>.Nothing(), Maybe<TDbDto>.Nothing()));
                                }
                            }
                            else if (mutation.Type == DictionaryMutationType.Remove)
                            {
                                var preExistingValue = GetDbSet(context).Find(mutation.Key);
                                if (preExistingValue != null)
                                {
                                    context.Entry(preExistingValue).State = EntityState.Detached;
                                    GetDbSet(context).Remove(preExistingValue);
                                    finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateRemove(mutation.Key, preExistingValue.ToMaybe()));
                                }
                                else
                                {
                                    throw new InvalidOperationException("Cannot remove an item when no item with that key already exists");
                                }
                            }
                            else if (mutation.Type == DictionaryMutationType.TryRemove)
                            {
                                var preExistingValue = GetDbSet(context).Find(mutation.Key);
                                if (preExistingValue != null)
                                {
                                    context.Entry(preExistingValue).State = EntityState.Detached;
                                    GetDbSet(context).Remove(preExistingValue);
                                    finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateRemove(mutation.Key, preExistingValue.ToMaybe()));
                                }
                                else
                                {
                                    finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateRemove(mutation.Key, Maybe<TDbDto>.Nothing()));
                                }
                            }
                            else if (mutation.Type == DictionaryMutationType.AddOrUpdate)
                            {
                                var preExistingValue = GetDbSet(context).Find(mutation.Key);
                                if (preExistingValue != null)
                                {
                                    var oldPreExistingValue = _mapper.Map<TDbDto, TDbDto>(preExistingValue);
                                    var updatedValue = mutation.ValueIfUpdating.Value(preExistingValue);
                                    _mapper.Map(updatedValue, preExistingValue);
                                    GetDbSet(context).Update(preExistingValue);
                                    finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateAddOrUpdate(mutation.Key, DictionaryItemAddOrUpdateResult.Update, oldPreExistingValue.ToMaybe(), preExistingValue));
                                }
                                else
                                {
                                    var updatedValue = mutation.ValueIfAdding.Value();
                                    GetDbSet(context).Add(updatedValue);
                                    finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateAddOrUpdate(mutation.Key, DictionaryItemAddOrUpdateResult.Update, Maybe<TDbDto>.Nothing(), updatedValue));
                                }
                            }
                        }

                        context.SaveChanges();
                        transaction.Commit();
                    }
                    catch (Exception e)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}