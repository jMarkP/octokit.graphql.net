﻿using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Octokit.GraphQL.Core;
using Octokit.GraphQL.Internal;

namespace Octokit.GraphQL
{
    public static class QueryableValueExtensions
    {
        public static readonly MethodInfo SelectMethod = GetMethodInfo(nameof(SelectMethod));
        public static readonly MethodInfo SelectListMethod = GetMethodInfo(nameof(SelectListMethod));
        public static readonly MethodInfo SingleMethod = GetMethodInfo(nameof(SingleMethod));
        public static readonly MethodInfo SingleOrDefaultMethod = GetMethodInfo(nameof(SingleOrDefaultMethod));

        [MethodId(nameof(SelectMethod))]
        public static IQueryableValue<TResult> Select<TValue, TResult>(
            this IQueryableValue<TValue> source,
            Expression<Func<TValue, TResult>> selector)
                where TValue : IQueryableValue
        {
            return new QueryableValue<TResult>(
                Expression.Call(
                    null,
                    GetMethodInfoOf(() => Select(
                        default(IQueryableValue<TValue>),
                        default(Expression<Func<TValue, TResult>>))),
                    new Expression[] { source.Expression, Expression.Quote(selector) }));
        }

        [MethodId(nameof(SelectListMethod))]
        public static IQueryableList<TResult> Select<TValue, TResult>(
            this IQueryableValue<TValue> source,
            Expression<Func<TValue, IQueryableList<TResult>>> selector)
                where TValue : IQueryableValue
        {
            return new QueryableList<TResult>(
                Expression.Call(
                    null,
                    GetMethodInfoOf(() => Select(
                        default(IQueryableValue<TValue>),
                        default(Expression<Func<TValue, IQueryableList<TResult>>>))),
                    new Expression[] { source.Expression, Expression.Quote(selector) }));
        }

        [MethodId(nameof(SingleMethod))]
        public static TValue Single<TValue>(this IQueryableValue<TValue> source)
        {
            throw new NotImplementedException();
        }

        [MethodId(nameof(SingleOrDefaultMethod))]
        public static TValue SingleOrDefault<TValue>(this IQueryableValue<TValue> source)
        {
            throw new NotImplementedException();
        }

        private static MethodInfo GetMethodInfo(string id)
        {
            return typeof(QueryableValueExtensions)
                .GetTypeInfo()
                .DeclaredMethods
                .Where(x => x.GetCustomAttribute<MethodIdAttribute>()?.Id == id)
                .SingleOrDefault();
        }

        private static MethodInfo GetMethodInfoOf<T>(Expression<Func<T>> expression)
        {
            var body = (MethodCallExpression)expression.Body;
            return body.Method;
        }
    }
}