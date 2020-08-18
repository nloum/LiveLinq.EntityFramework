using System;
using AutoMapper;
using ComposableCollections.Dictionary;

namespace LiveLinq.EntityFramework
{
    public static class Extensions
    {
        /// <summary>
        /// Creates a facade on top of the specified IComposableDictionary that keeps tracks of changes and occasionally
        /// flushes them to the specified IComposableDictionary.
        /// </summary>
        public static IComposableDictionary<TKey, TValue> WithMapping<TKey, TValue, TInnerValue>(this IComposableDictionary<TKey, TInnerValue> source, Func<IRuntimeMapper> mapper) where TValue : class
        {
            return new AnonymousMapDictionary<TKey, TValue, TInnerValue>(source, (id, value) => mapper().Map<TValue, TInnerValue>(value), (id, value) => mapper().Map<TInnerValue, TValue>(value));
        }
    }
}