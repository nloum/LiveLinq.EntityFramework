using System;
using ComposableCollections.Dictionary;
using Microsoft.EntityFrameworkCore;

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

        public DatabaseLayer(Func<TDbContext> create)
        {
            _create = create;
        }

        public IComposableDictionary<TId, TDbDto> WithAggregateRoot<TId, TDbDto>(Func<TDbContext, DbSet<TDbDto>> dbSet,
            Func<TDbDto, TId> id, Func<DbSet<TDbDto>, TId, TDbDto> find) where TDbDto : class
        {
            return new AnonymousEntityFrameCoreDictionary<TId, TDbDto, TDbContext>(dbSet, _create, id, find);
        }
        
        public IComposableDictionary<TId, TDbDto> WithAggregateRoot<TId, TDbDto>(Func<TDbContext, DbSet<TDbDto>> dbSet,
            Func<TDbDto, TId> id) where TDbDto : class
        {
            return new AnonymousEntityFrameCoreDictionary<TId, TDbDto, TDbContext>(dbSet, _create, id);
        }
    }
}