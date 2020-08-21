using System;
using AutoMapper;
using ComposableCollections.Dictionary;
using Microsoft.EntityFrameworkCore;

namespace LiveLinq.EntityFramework
{
    public static class Extensions
    {
        public static IComposableDictionary<TId, TDbDto> AsComposableDictionary<TId, TDbDto, TDbContext>(this TDbContext dbContext, Func<TDbContext, DbSet<TDbDto>> dbSet, Func<TDbDto, TId> dbDtoId, Func<DbSet<TDbDto>, TId, TDbDto> find = null) where TDbContext : DbContext where TDbDto : class
        {
            var theDbSet = dbSet(dbContext);
            if (find == null)
            {
                find = (x, theId) => x.Find(theId);
            }
            
            var efCoreDict = new AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext>(dbContext, theDbSet,
                dbDtoId, find);
            return efCoreDict;
        }

        public static IComposableReadOnlyDictionary<TId, TDbDto> AsComposableReadOnlyDictionary<TId, TDbDto, TDbContext>(this TDbContext dbContext, Func<TDbContext, DbSet<TDbDto>> dbSet, Func<TDbDto, TId> dbDtoId, Func<DbSet<TDbDto>, TId, TDbDto> find = null) where TDbContext : DbContext where TDbDto : class
        {
            var theDbSet = dbSet(dbContext);
            if (find == null)
            {
                find = (x, theId) => x.Find(theId);
            }
            
            var efCoreDict = new AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext>(dbContext, theDbSet,
                dbDtoId, find);
            return efCoreDict;
        }

        // public static IDisposableTransactionalCollection<IDisposableReadOnlyDictionary<TId, TDbDto>, IDisposableDictionary<TId, TDbDto>> AsTransactionalDictionary<TId, TDbDto, TDbContext>(this TDbContext dbContext, Func<TDbContext, DbSet<TDbDto>> dbSet, Func<TDbDto, TId> dbDtoId, Func<DbSet<TDbDto>, TId, TDbDto> find = null) where TDbContext : DbContext where TDbDto : class
        // {
        //     var theDbSet = dbSet(dbContext);
        //     if (find == null)
        //     {
        //         find = (x, theId) => x.Find(theId);
        //     }
        //     
        //     var result = new AnonymousDisposableTransactionalCollection<IDisposableReadOnlyDictionary<TId, TDbDto>, IDisposableDictionary<TId, TDbDto>>(() =>
        //     {
        //         var efCoreDict = new AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext>(dbContext, theDbSet,
        //             dbDtoId, find);
        //         return new DisposableReadOnlyDictionaryDecorator<TId, TDbDto>(efCoreDict, dbContext);
        //     }, () =>
        //     {
        //         var efCoreDict = new AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext>(dbContext, theDbSet,
        //             dbDtoId, find);
        //         return new DisposableDictionaryDecorator<TId, TDbDto>(efCoreDict, dbContext);
        //     }, dbContext);
        //     return result;
        // }
        //
        // public static IDisposableReadOnlyTransactionalCollection<IDisposableReadOnlyDictionary<TId, TDbDto>> AsReadOnlyTransactionalDictionary<TId, TDbDto, TDbContext>(this TDbContext dbContext, Func<TDbContext, DbSet<TDbDto>> dbSet, Func<TDbDto, TId> dbDtoId, Func<DbSet<TDbDto>, TId, TDbDto> find = null) where TDbContext : DbContext where TDbDto : class
        // {
        //     var theDbSet = dbSet(dbContext);
        //     if (find == null)
        //     {
        //         find = (x, theId) => x.Find(theId);
        //     }
        //     
        //     var result = new AnonymousDisposableReadOnlyTransactionalCollection<IDisposableReadOnlyDictionary<TId, TDbDto>>(() =>
        //     {
        //         var efCoreDict = new AnonymousEntityFrameworkCoreDictionary<TId, TDbDto, TDbContext>(dbContext, theDbSet,
        //             dbDtoId, find);
        //         return new DisposableReadOnlyDictionaryDecorator<TId, TDbDto>(efCoreDict, dbContext);
        //     }, dbContext);
        //     
        //     return result;
        // }
    }
}