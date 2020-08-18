using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LiveLinq.EntityFramework
{
    public class AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext> : EntityFrameworkCoreDictionaryBase<TId, TDbDto, TDbContext> where TDbDto : class where TDbContext : DbContext
    {
        private readonly Func<TDbContext, DbSet<TDbDto>> _getDbSet;
        private readonly Func<TDbContext> _createDbContext;
        private readonly Func<TDbDto, TId> _getId;
        private readonly Func<DbSet<TDbDto>, TId, TDbDto> _find;

        public AnonymousEntityFrameworkCoreDictionary(Func<TDbContext, DbSet<TDbDto>> getDbSet, Func<TDbContext> createDbContext, Func<TDbDto, TId> getId, Func<DbSet<TDbDto>, TId, TDbDto> find = null) : base(false)
        {
            _find = find;
            if (_find == null)
            {
                _find = (dbSet, id) => dbSet.Find(id);
            }
            
            _getDbSet = getDbSet ?? throw new ArgumentNullException(nameof(getDbSet));
            _createDbContext = createDbContext ?? throw new ArgumentNullException(nameof(createDbContext));
            _getId = getId ?? throw new ArgumentNullException(nameof(getId));
        }

        protected override DbSet<TDbDto> GetDbSet(TDbContext context)
        {
            return _getDbSet(context);
        }

        protected override TDbContext CreateDbContext()
        {
            return _createDbContext();
        }

        protected override TId GetId(TDbDto dbDto)
        {
            return _getId(dbDto);
        }

        protected override TDbDto Find(DbSet<TDbDto> dbSet, TId id)
        {
            return _find(dbSet, id);
        }

        protected override void Migrate(DatabaseFacade database)
        {
            throw new NotImplementedException();
        }
    }
}