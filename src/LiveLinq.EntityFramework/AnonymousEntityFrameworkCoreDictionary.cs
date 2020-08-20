using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LiveLinq.EntityFramework
{
    public class AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext> : EntityFrameworkCoreDictionaryBase<TId, TDbDto, TDbContext> where TDbDto : class where TDbContext : DbContext
    {
        private readonly Func<TDbDto, TId> _getId;
        private readonly Func<DbSet<TDbDto>, TId, TDbDto> _find;

        public AnonymousEntityFrameworkCoreDictionary(TDbContext dbContext,
            DbSet<TDbDto> dbSet, Func<TDbDto, TId> getId,
            Func<DbSet<TDbDto>, TId, TDbDto> find = null) : base(dbContext, dbSet)
        {
            _find = find;
            if (_find == null)
            {
                _find = (dbSet2, id) => dbSet2.Find(id);
            }
            
            _getId = getId ?? throw new ArgumentNullException(nameof(getId));
        }

        protected override TId GetId(TDbDto dbDto)
        {
            return _getId(dbDto);
        }

        protected override TDbDto Find(DbSet<TDbDto> dbSet, TId id)
        {
            return _find(dbSet, id);
        }
    }
}