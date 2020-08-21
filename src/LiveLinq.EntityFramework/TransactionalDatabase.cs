using System;
using ComposableCollections.Dictionary;
using Microsoft.EntityFrameworkCore;

namespace LiveLinq.EntityFramework
{
    public class TransactionalDatabase
    {
        public static ITransactionalCollection<TDbContext, TDbContext> Create<TDbContext>(Func<TDbContext> create,
            Action<TDbContext> migrate = null) where TDbContext : DbContext
        {
            if (migrate != null)
            {
                var hasMigratedYet = false;
                var simpleCreate = create;
                create = () =>
                {
                    if (!hasMigratedYet)
                    {
                        using (var context = simpleCreate())
                        {
                            migrate(context);
                        }
                    }

                    return simpleCreate();
                };
            }
            
            return TransactionalCollection.Create(create, create);
        }
    }
}