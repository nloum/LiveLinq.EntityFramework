﻿using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LiveLinq.EntityFramework
{
    public class AnonymousEntityFrameCoreDictionary<TId, TDbDto, TDbContext> : EntityFrameworkCoreDictionaryBase<TId, TDbDto, TDbContext> where TDbDto : class where TDbContext : DbContext
    {
        private readonly Func<TDbContext, DbSet<TDbDto>> _getDbSet;
        private readonly Func<TDbContext> _createDbContext;
        private readonly Func<TDbDto, TId> _getId;
        private readonly Action<DatabaseFacade> _migrate;

        public AnonymousEntityFrameCoreDictionary(Func<TDbContext, DbSet<TDbDto>> getDbSet, Func<TDbContext> createDbContext, Func<TDbDto, TId> getId, Action<DatabaseFacade> migrate = null) : base(migrate != null)
        {
            _getDbSet = getDbSet ?? throw new ArgumentNullException(nameof(getDbSet));
            _createDbContext = createDbContext ?? throw new ArgumentNullException(nameof(createDbContext));
            _getId = getId ?? throw new ArgumentNullException(nameof(getId));
            _migrate = migrate;
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

        protected override void Migrate(DatabaseFacade database)
        {
            _migrate(database);
        }
    }
}