using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AutoMapper;
using ComposableCollections.Dictionary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SimpleMonads;

namespace LiveLinq.EntityFramework
{
    public abstract class EntityFrameworkCoreDictionaryBase<TId, TDbDto, TDbContext> : DictionaryBase<TId, TDbDto> where TDbDto : class where TDbContext : DbContext
    {
        private readonly TDbContext _dbContext;
        private readonly DbSet<TDbDto> _dbSet;
        private readonly IMapper _mapper;

        protected abstract TId GetId(TDbDto dbDto);
        protected abstract TDbDto Find(DbSet<TDbDto> dbSet, TId id);

        protected EntityFrameworkCoreDictionaryBase(TDbContext dbContext, DbSet<TDbDto> dbSet)
        {
            _dbContext = dbContext;
            _dbSet = dbSet;
            var mapperConfig = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<TDbDto, TDbDto>();
            });

            _mapper = mapperConfig.CreateMapper();
        }

        public override bool TryGetValue(TId key, out TDbDto value)
        {
            value = Find(_dbSet, key);
            return value != null;
        }

        public override IEnumerator<IKeyValue<TId, TDbDto>> GetEnumerator()
        {
            return _dbSet.AsEnumerable().Select(value => new KeyValue<TId, TDbDto>(GetId(value), value)).ToImmutableList().GetEnumerator();
        }

        public override int Count => _dbSet.Count();

        public override IEqualityComparer<TId> Comparer { get; } = EqualityComparer<TId>.Default;

        public override IEnumerable<TId> Keys => this.Select(kvp => kvp.Key);

        public override IEnumerable<TDbDto> Values => this.Select(kvp => kvp.Value);
        public override void Mutate(IEnumerable<DictionaryMutation<TId, TDbDto>> mutations, out IReadOnlyList<DictionaryMutationResult<TId, TDbDto>> results)
        {
            var finalResults = new List<DictionaryMutationResult<TId, TDbDto>>();
            results = finalResults;

            using (var transaction = _dbContext.Database.BeginTransaction())
            {
                try
                {
                    foreach (var mutation in mutations)
                    {
                        if (mutation.Type == DictionaryMutationType.Add)
                        {
                            var preExistingValue = Find(_dbSet, mutation.Key);
                            if (preExistingValue != null)
                            {
                                _dbContext.Entry(preExistingValue).State = EntityState.Detached;
                                throw new InvalidOperationException("Cannot add an item when an item with that key already exists");
                            }

                            var newValue = mutation.ValueIfAdding.Value();
                            _dbSet.Add(newValue);
                            finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateAdd(mutation.Key, true, Maybe<TDbDto>.Nothing(), newValue.ToMaybe()));
                        }
                        else if (mutation.Type == DictionaryMutationType.TryAdd)
                        {
                            var preExistingValue = Find(_dbSet, mutation.Key);
                            if (preExistingValue != null)
                            {
                                _dbContext.Entry(preExistingValue).State = EntityState.Detached;
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateAdd(mutation.Key, false, preExistingValue.ToMaybe(), Maybe<TDbDto>.Nothing()));
                            }
                            else
                            {
                                var newValue = mutation.ValueIfAdding.Value();
                                _dbSet.Add(newValue);
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateAdd(mutation.Key, true, Maybe<TDbDto>.Nothing(), newValue.ToMaybe()));
                            }
                        }
                        else if (mutation.Type == DictionaryMutationType.Update)
                        {
                            var preExistingValue = Find(_dbSet, mutation.Key);
                            if (preExistingValue != null)
                            {
                                var oldPreExistingValue = _mapper.Map<TDbDto, TDbDto>(preExistingValue);

                                var updatedValue = mutation.ValueIfUpdating.Value(preExistingValue);
                                _mapper.Map(updatedValue, preExistingValue);
                                _dbSet.Update(preExistingValue);
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateUpdate(mutation.Key, true, oldPreExistingValue.ToMaybe(), preExistingValue.ToMaybe()));
                            }
                            else
                            {
                                throw new InvalidOperationException("Cannot update an item when no item with that key already exists");
                            }
                        }
                        else if (mutation.Type == DictionaryMutationType.TryUpdate)
                        {
                            var preExistingValue = Find(_dbSet, mutation.Key);
                            if (preExistingValue != null)
                            {
                                var oldPreExistingValue = _mapper.Map<TDbDto, TDbDto>(preExistingValue);
                                var updatedValue = mutation.ValueIfUpdating.Value(preExistingValue);
                                _mapper.Map(updatedValue, preExistingValue);
                                _dbSet.Update(preExistingValue);
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateUpdate(mutation.Key, true, oldPreExistingValue.ToMaybe(), preExistingValue.ToMaybe()));
                            }
                            else
                            {
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateUpdate(mutation.Key, true, Maybe<TDbDto>.Nothing(), Maybe<TDbDto>.Nothing()));
                            }
                        }
                        else if (mutation.Type == DictionaryMutationType.Remove)
                        {
                            var preExistingValue = Find(_dbSet, mutation.Key);
                            if (preExistingValue != null)
                            {
                                _dbContext.Entry(preExistingValue).State = EntityState.Detached;
                                _dbSet.Remove(preExistingValue);
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateRemove(mutation.Key, preExistingValue.ToMaybe()));
                            }
                            else
                            {
                                throw new InvalidOperationException("Cannot remove an item when no item with that key already exists");
                            }
                        }
                        else if (mutation.Type == DictionaryMutationType.TryRemove)
                        {
                            var preExistingValue = Find(_dbSet, mutation.Key);
                            if (preExistingValue != null)
                            {
                                _dbContext.Entry(preExistingValue).State = EntityState.Detached;
                                _dbSet.Remove(preExistingValue);
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateRemove(mutation.Key, preExistingValue.ToMaybe()));
                            }
                            else
                            {
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateRemove(mutation.Key, Maybe<TDbDto>.Nothing()));
                            }
                        }
                        else if (mutation.Type == DictionaryMutationType.AddOrUpdate)
                        {
                            var preExistingValue = Find(_dbSet, mutation.Key);
                            if (preExistingValue != null)
                            {
                                var oldPreExistingValue = _mapper.Map<TDbDto, TDbDto>(preExistingValue);
                                var updatedValue = mutation.ValueIfUpdating.Value(preExistingValue);
                                _mapper.Map(updatedValue, preExistingValue);
                                _dbSet.Update(preExistingValue);
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateAddOrUpdate(mutation.Key, DictionaryItemAddOrUpdateResult.Update, oldPreExistingValue.ToMaybe(), preExistingValue));
                            }
                            else
                            {
                                var updatedValue = mutation.ValueIfAdding.Value();
                                _dbSet.Add(updatedValue);
                                finalResults.Add(DictionaryMutationResult<TId, TDbDto>.CreateAddOrUpdate(mutation.Key, DictionaryItemAddOrUpdateResult.Update, Maybe<TDbDto>.Nothing(), updatedValue));
                            }
                        }
                    }

                    _dbContext.SaveChanges();
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