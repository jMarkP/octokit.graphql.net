﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Octokit.GraphQL.Core.Syntax;
using Octokit.GraphQL.Core.Utilities;
using Newtonsoft.Json.Linq;

namespace Octokit.GraphQL.Core.Builders
{
    public class QueryBuilder : ExpressionVisitor
    {
        const int MaxPageSize = 100;
        static readonly ParameterExpression RootDataParameter = Expression.Parameter(typeof(JObject), "data");

        OperationDefinition root;
        Expression rootExpression;
        SyntaxTree syntax;
        Dictionary<ParameterExpression, LambdaParameter> lambdaParameters;
        List<SubQuery> subqueries = new List<SubQuery>();

        public ICompiledQuery<TResult> Build<TResult>(IQueryableValue<TResult> query)
        {
            Initialize();

            var rewritten = Visit(query.Expression);
            var lambda = Expression.Lambda<Func<JObject, TResult>>(
                rewritten.AddCast(typeof(TResult)),
                RootDataParameter);
            var master = new CompiledQuery<TResult>(root, lambda);
            return subqueries.Count == 0 ? 
                (ICompiledQuery<TResult>)master :
                new PagedQuery<TResult>(master, subqueries);
        }

        public ICompiledQuery<IEnumerable<TResult>> Build<TResult>(IQueryableList<TResult> query)
        {
            Initialize();

            var rewritten = Visit(query.Expression);
            var lambda = Expression.Lambda<Func<JObject, IEnumerable<TResult>>>(
                rewritten.AddCast(typeof(IEnumerable<TResult>)),
                RootDataParameter);
            var master = new CompiledQuery<IEnumerable<TResult>>(root, lambda);
            return subqueries.Count == 0 ?
                (ICompiledQuery<IEnumerable<TResult>>)master :
                new PagedQuery<IEnumerable<TResult>>(master, subqueries);
        }

        private ICompiledQuery Build(Expression expression)
        {
            Initialize();

            var rewritten = Visit(expression);
            var returnType = typeof(IEnumerable<>).MakeGenericType(expression.Type.GenericTypeArguments[0]);
            var lambda = Expression.Lambda(
                rewritten.AddCast(returnType),
                RootDataParameter);
            var queryType = typeof(CompiledQuery<>).MakeGenericType(returnType);
            var master = queryType.GetTypeInfo()
                .DeclaredConstructors
                .Single()
                .Invoke(new object[] { root, lambda });
            return (ICompiledQuery)master;
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            var left = BookmarkAndVisit(node.Left);
            var right = BookmarkAndVisit(node.Right);
            var leftNull = IsNullConstant(node.Left);
            var rightNull = IsNullConstant(node.Right);

            if ((node.NodeType == ExpressionType.Equal || node.NodeType == ExpressionType.NotEqual))
            {
                // When we're comparing with null, we need to transform an equality conditional such as
                // `token["foo"] == null` to `token["foo"].Type == JTokenType.Null`
                if (!leftNull && rightNull)
                {
                    return Expression.MakeBinary(
                        node.NodeType,
                        Expression.Property(left, nameof(JToken.Type)),
                        Expression.Constant(JTokenType.Null));
                }
                else if(leftNull && !rightNull)
                {
                    return Expression.MakeBinary(
                        node.NodeType,
                        Expression.Constant(JTokenType.Null),
                        Expression.Property(right, nameof(JToken.Type)));
                }
            }

            return node.Update(
                left.AddCast(node.Left.Type),
                node.Conversion,
                right.AddCast(node.Right.Type));
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (syntax.Root == null)
            {
                var query = node.Value as IQuery;
                var mutation = node.Value as IMutation;
                var queryEntity = node.Value as IQueryableValue;

                rootExpression = node;

                if (query != null)
                {
                    root = syntax.AddRoot(OperationType.Query, null);
                    return RootDataParameter.AddIndexer("data");
                }
                else if (mutation != null)
                {
                    root = syntax.AddRoot(OperationType.Mutation, null);
                    return RootDataParameter.AddIndexer("data");
                }
                else if (queryEntity != null)
                {
                    return Visit(queryEntity.Expression);
                }
            }

            return node;
        }

        protected override Expression VisitConditional(ConditionalExpression node)
        {
            var test = Visit(node.Test);
            var ifTrue = Visit(node.IfTrue);
            var ifFalse = Visit(node.IfFalse);
            var trueNull = IsNullConstant(ifTrue);
            var falseNull = IsNullConstant(ifFalse);

            if (!trueNull)
            {
                ifTrue = ifTrue.AddCast(node.Type);
            }
            else if (!falseNull)
            {
                ifTrue = Expression.Constant(null, ifFalse.Type);
            }

            if (!falseNull)
            {
                ifFalse = ifFalse.AddCast(node.Type);
            }
            else if (!trueNull)
            {
                ifFalse = Expression.Constant(null, ifTrue.Type);
            }

            return Expression.Condition(
                test,
                ifTrue,
                ifFalse,
                !IsNullConstant(ifTrue) ? ifTrue.Type : ifFalse.Type);
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            var parameters = RewriteParameters(node.Parameters);
            var body = Visit(node.Body);
            return Expression.Lambda(body, node.ToString(), parameters);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            return VisitMember(node, null);
        }

        protected override MemberAssignment VisitMemberAssignment(MemberAssignment node)
        {
            var expression = BookmarkAndVisit(node.Expression).AddCast(node.Expression.Type);
            return Expression.Bind(node.Member, expression);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            return VisitMethodCall(node, null);
        }

        protected override Expression VisitNew(NewExpression node)
        {
            var newArguments = new List<Expression>();
            var index = 0;

            foreach (var arg in node.Arguments)
            {
                using (syntax.Bookmark())
                {
                    var alias = node.Members?[index];
                    Expression rewritten;

                    if (arg is MemberExpression member)
                    {
                        rewritten = VisitMember(member, alias);
                    }
                    else if (arg is MethodCallExpression call)
                    {
                        rewritten = VisitMethodCall(call, alias);
                    }
                    else
                    {
                        rewritten = Visit(arg);
                    }

                    newArguments.Add(rewritten.AddCast(arg.Type));
                }

                ++index;
            }

            return node.Members != null ?
                Expression.New(node.Constructor, newArguments, node.Members) :
                Expression.New(node.Constructor, newArguments);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (IsQueryableValue(node.Type))
            {
                LambdaParameter result;

                if (lambdaParameters.TryGetValue(node, out result))
                {
                    return result.Rewritten;
                }
                else
                {
                    throw new Exception(
                        "Internal Error: encountered a lambda parameter that hasn't previously been rewritten.");
                }
            }

            return node;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Convert)
            {
                var rewritten = Visit(node.Operand);

                if (rewritten.Type == typeof(JToken))
                {
                    return Expression.Convert(
                        rewritten.AddCast(node.Operand.Type),
                        node.Type);
                }
            }

            return node.Update(Visit(node.Operand));
        }

        private void Initialize()
        {
            root = null;
            syntax = new SyntaxTree();
            lambdaParameters = new Dictionary<ParameterExpression, LambdaParameter>();
        }

        private Expression VisitMember(MemberExpression node, MemberInfo alias)
        {
            if (IsUnionMember(node.Member))
            {
                var source = Visit(node.Expression);
                var fragment = syntax.AddInlineFragment(((PropertyInfo)node.Member).PropertyType, true);
                return Expression.Call(
                    Rewritten.Value.OfTypeMethod,
                    source,
                    Expression.Constant(fragment.TypeCondition.Name));
            }
            else if (IsQueryableValueMember(node.Member))
            {
                var expression = node.Expression;
                SubQuery subQuery = null;

                if (expression is SubqueryExpression subQueryExpression)
                {
                    // This signals that a parent query has designated this method call as the
                    // paging method for a subquery. Mark it as such and get the real method call
                    // expression.
                    subQuery = subQueryExpression.SubQuery;
                    expression = subQueryExpression.MethodCall;
                }

                if (expression is ParameterExpression parameterExpression)
                {
                    var parameter = (ParameterExpression)Visit(parameterExpression);
                    var parentSelection = GetSelectionSet(parameterExpression);
                    var field = syntax.AddField(parentSelection, node.Member, alias);
                    return Visit(parameterExpression).AddIndexer(field);
                }
                else
                {
                    var instance = Visit(expression);

                    if (subQuery != null)
                    {
                        // This is the paging method for a subquery: add the required `pageInfo`
                        // field selections.
                        var pageInfo = PageInfoSelection();
                        syntax.Head.Selections.Add(pageInfo);

                        // And store an expression to read the `pageInfo` from the subquery.
                        var selections = syntax.FieldStack
                            .Select(x => x.Name)
                            .Concat(new[] { "pageInfo" });
                        subQuery.PageInfo = CreateSelectorExpression(selections);
                    }

                    var field = syntax.AddField(node.Member, alias);
                    return instance.AddIndexer(field);
                }
            }
            else
            {
                var instance = Visit(node.Expression);

                if (ExpressionWasRewritten(node.Expression, instance))
                {
                    instance = instance.AddCast(node.Expression.Type);
                }

                return node.Update(instance);
            }
        }

        private static Expression<Func<JObject, JToken>> CreateSelectorExpression(IEnumerable<string> selectors)
        {
            var parameter = Expression.Parameter(typeof(JObject), "data");
            var expression = Expression.Call(
                    parameter,
                    JsonMethods.JTokenIndexer,
                    Expression.Constant("data"));

            foreach (var s in selectors)
            {
                expression = Expression.Call(
                    expression,
                    JsonMethods.JTokenIndexer,
                    Expression.Constant(s));
            }

            return Expression.Lambda<Func<JObject, JToken>>(expression, parameter);
        }

        private IEnumerable<Expression> VisitMethodArguments(MethodInfo method, ReadOnlyCollection<Expression> arguments)
        {
            var parameters = method.GetParameters();
            var index = 0;

            foreach (var arg in arguments)
            {
                var parameter = parameters[index++];
                yield return Visit(arg).AddCast(parameter.ParameterType);
            }
        }

        private Expression VisitMethodCall(MethodCallExpression node, MemberInfo alias)
        {
            if (node.Method.DeclaringType == typeof(QueryableValueExtensions))
            {
                return RewriteValueExtension(node);
            }
            else if (node.Method.DeclaringType == typeof(QueryableListExtensions))
            {
                return RewriteListExtension(node);
            }
            else if (node.Method.DeclaringType == typeof(QueryableInterfaceExtensions))
            {
                return RewriteInterfaceExtension(node);
            }
            else if (node.Method.DeclaringType == typeof(PagingConnectionExtensions))
            {
                return RewritePagingConnectionExtensions(node);
            }
            else if (IsQueryableValueMember(node.Method))
            {
                return VisitQueryMethod(node, alias);
            }
            else
            {
                try
                {
                    var methodCallExpression = node.Update(Visit(node.Object), VisitMethodArguments(node.Method, node.Arguments));
                    return methodCallExpression;
                }
                catch (NotSupportedException ex)
                {
                    throw new NotSupportedException($"{node.Method.Name}() is not supported", ex);
                }
            }
        }

        private Expression RewriteValueExtension(MethodCallExpression expression)
        {
            if (expression.Method.GetGenericMethodDefinition() == QueryableValueExtensions.SelectMethod)
            {
                var source = expression.Arguments[0];
                var selectExpression = expression.Arguments[1];
                var lambda = selectExpression.GetLambda();
                var instance = Visit(source);
                var select = (LambdaExpression)Visit(lambda);

                return Expression.Call(
                    Rewritten.Value.SelectMethod.MakeGenericMethod(select.ReturnType),
                    instance,
                    select);
            }
            else if (expression.Method.GetGenericMethodDefinition() == QueryableValueExtensions.SelectListMethod)
            {
                var source = expression.Arguments[0];
                var selectExpression = expression.Arguments[1];
                var lambda = selectExpression.GetLambda();
                var instance = Visit(source);
                var select = (LambdaExpression)Visit(lambda);
                var itemType = select.ReturnType == typeof(JToken) ?
                    select.ReturnType :
                    GetEnumerableItemType(select.ReturnType);

                return Expression.Call(
                    Rewritten.Value.SelectListMethod.MakeGenericMethod(itemType),
                    instance,
                    select);
            }
            else if (expression.Method.GetGenericMethodDefinition() == QueryableValueExtensions.SingleMethod)
            {
                var source = expression.Arguments[0];
                var instance = Visit(source);

                return Expression.Call(
                    Rewritten.Value.SingleMethod.MakeGenericMethod(instance.Type),
                    instance);
            }
            else if (expression.Method.GetGenericMethodDefinition() == QueryableValueExtensions.SingleOrDefaultMethod)
            {
                var source = expression.Arguments[0];
                var instance = Visit(source);

                return Expression.Call(
                    Rewritten.Value.SingleOrDefaultMethod.MakeGenericMethod(instance.Type),
                    instance);
            }
            else
            {
                throw new NotSupportedException($"{expression.Method.Name}() is not supported");
            }
        }

        private Expression RewriteListExtension(MethodCallExpression expression)
        {
            if (expression.Method.GetGenericMethodDefinition() == QueryableListExtensions.SelectMethod)
            {
                var source = expression.Arguments[0];
                var selectExpression = expression.Arguments[1];
                var lambda = selectExpression.GetLambda();
                var instance = Visit(source);

                if (instance is AllPagesExpression allPages)
                {
                    // .AllPages() was called on the instance. The expression is just a marker for
                    // this, the actual instance is in `allPages.Instance`
                    instance = Visit(allPages.Instance);

                    // Add a "first: 100" argument to the query field.
                    syntax.AddArgument("first", MaxPageSize);

                    // Add the required "pageInfo" field selections then select "nodes".
                    syntax.Head.Selections.Add(PageInfoSelection());
                    syntax.AddField("nodes");

                    // Create the subquery
                    AddSubquery(allPages.Instance, expression, instance.AddIndexer("pageInfo"));

                    // And continue the query as normal after selecting the "nodes" JToken.
                    instance = instance.AddIndexer("nodes");
                }
                
                
                var select = (LambdaExpression)Visit(lambda);
                return Expression.Call(
                    Rewritten.List.SelectMethod.MakeGenericMethod(select.ReturnType),
                    instance,
                    select);
            }
            else if (expression.Method.GetGenericMethodDefinition() == QueryableListExtensions.ToDictionaryMethod)
            {
                var instance = Visit(expression.Arguments[0]);
                var keySelect = expression.Arguments[1].GetLambda();
                var valueSelect = expression.Arguments[2].GetLambda();
                var inputType = GetEnumerableItemType(instance.Type);

                if (inputType == typeof(JToken))
                {
                    throw new NotImplementedException();
                }
                else
                {
                    return Expression.Call(
                        LinqMethods.ToDictionaryMethod.MakeGenericMethod(
                            inputType,
                            keySelect.ReturnType,
                            valueSelect.ReturnType),
                        instance,
                        keySelect,
                        valueSelect);
                }
            }
            else if (expression.Method.GetGenericMethodDefinition() == QueryableListExtensions.ToListMethod)
            {
                var source = expression.Arguments[0];
                var instance = Visit(source);
                var inputType = GetEnumerableItemType(instance.Type);
                var resultType = GetQueryableListItemType(source.Type);

                if (inputType == typeof(JToken))
                {
                    return Expression.Call(
                        Rewritten.List.ToListMethod.MakeGenericMethod(resultType),
                        instance);
                }
                else
                {
                    return Expression.Call(
                        LinqMethods.ToListMethod.MakeGenericMethod(resultType),
                        instance);
                }
            }
            else if (expression.Method.GetGenericMethodDefinition() == QueryableListExtensions.OfTypeMethod)
            {
                var source = expression.Arguments[0];
                var instance = Visit(source);
                var resultType = GetQueryableListItemType(source.Type);
                var fragment = syntax.AddInlineFragment(expression.Method.GetGenericArguments()[0], true);

                return Expression.Call(
                    Rewritten.List.OfTypeMethod,
                    instance,
                    Expression.Constant(fragment.TypeCondition.Name));
            }
            else
            {
                throw new NotSupportedException($"{expression.Method.Name}() is not supported");
            }
        }

        private Expression RewriteInterfaceExtension(MethodCallExpression expression)
        {
            if (expression.Method.GetGenericMethodDefinition() == QueryableInterfaceExtensions.CastMethod)
            {
                var source = expression.Arguments[0];
                var instance = Visit(source);
                var targetType = expression.Method.GetGenericArguments()[0];
                var fragment = syntax.AddInlineFragment(targetType, true);

                return Expression.Call(
                    Rewritten.Interface.CastMethod,
                    instance,
                    Expression.Constant(fragment.TypeCondition.Name));
            }
            else
            {
                throw new NotSupportedException($"{expression.Method.Name}() is not supported");
            }
        }

        private Expression RewritePagingConnectionExtensions(MethodCallExpression expression)
        {
            if (expression.Method.GetGenericMethodDefinition() == PagingConnectionExtensions.AllPagesMethod)
            {
                // .AllPages() was called on a IPagingConnection. We can't handle this yet -
                // return an AllPagesExpression so we know to handle it when the containing Select
                // is visited.
                return new AllPagesExpression((MethodCallExpression)expression.Arguments[0]);
            }
            else
            {
                throw new NotSupportedException($"{expression.Method.Name}() is not supported");
            }
        }

        private Expression VisitQueryMethod(MethodCallExpression node, MemberInfo alias)
        {
            var queryEntity = (node.Object as ConstantExpression)?.Value as IQueryableValue;
            var instance = Visit(queryEntity?.Expression ?? node.Object);
            var field = syntax.AddField(node.Method, alias);

            VisitQueryMethodArguments(node.Method.GetParameters(), node.Arguments);

            return instance.AddIndexer(field);
        }

        private void VisitQueryMethodArguments(ParameterInfo[] parameters, ReadOnlyCollection<Expression> arguments)
        {
            for (var i = 0; i < parameters.Length; ++i)
            {
                var parameter = parameters[i];
                var value = EvaluateValue(arguments[i]);

                if (value is IArg arg)
                {
                    if (arg.VariableName == null)
                    {
                        value = arg.Value;
                    }
                    else
                    {
                        value = syntax.AddVariableDefinition(arg.Type, arg.IsNullableVariable, arg.VariableName);
                    }
                }

                if (value != null)
                {
                    syntax.AddArgument(parameter.Name, value);
                }
            }
        }

        private void AddSubquery(
            MethodCallExpression expression,
            MethodCallExpression selector,
            Expression pageInfoSelector)
        {
            var subQuery = new SubQuery();

            // Create the actual subquery.
            var nodeQuery = CreateNodeQuery(subQuery, expression, selector);
            var subQueryBuilder = new QueryBuilder();
            subQuery.Query = subQueryBuilder.Build(nodeQuery);

            // Create a lambda that selects the "pageInfo" field.
            subQuery.ParentPageInfo = Expression.Lambda<Func<JObject, JToken>>(
                pageInfoSelector,
                RootDataParameter);
            
            subqueries.Add(subQuery);
        }

        private Expression CreateNodeQuery(
            SubQuery subQuery,
            MethodCallExpression expression,
            MethodCallExpression selector)
        {
            // Given an expression such as:
            //
            // new Query()
            //   .Repository("foo", "bar")
            //   .Issues()
            //   .AllPages()
            //   .Select(x => x.Name);
            //
            // This method creates a subquery expression that reads:
            //
            // new Query()
            //   .Node(Var("__id"))
            //   .Cast<Repository>
            //   .Issues(first: 100, after: Var("__after"))
            //   .Select(x => x.Name);
            //
            // The passed in `expression` parameter is the part before `AllPages()` and the
            // `selector` parameter is the `x => x.Name` selector.

            // Get the `Repository` type in the example above.
            var nodeType = expression.Object.Type;

            // First create the expression `new Query().Node(Var("__id"))`
            Expression rewritten = Expression.Call(
                rootExpression,
                "Node",
                null,
                Expression.Constant(new Arg<ID>("__id", false)));

            // Add `.Cast<nodeType>`.
            rewritten = Expression.Call(
                QueryableInterfaceExtensions.CastMethod.MakeGenericMethod(nodeType),
                rewritten);

            // Rewrite the method to add the `first: 100` and `after: Var("__after")`
            // parameters, and make it be called on `rewritten`.
            var methodCall = RewritePagingMethodCall(expression, rewritten);

            // Wrap this in a SubqueryExpression to instruct the child query builder to add the
            // paging infrastructure.
            rewritten = new SubqueryExpression(subQuery, methodCall);

            // Add "nodes"
            rewritten = Expression.Property(rewritten, "Nodes");

            // And now add in the selector.
            return selector.Update(
                null,
                new[] { rewritten, selector.Arguments[1] });
        }

        MethodCallExpression RewritePagingMethodCall(
            MethodCallExpression methodCall,
            Expression instance)
        {
            var arguments = new List<Expression>();

            foreach (var parameter in methodCall.Method.GetParameters())
            {
                object value = null;

                switch (parameter.Name)
                {
                    case "first":
                        value = new Arg<int>(MaxPageSize);
                        break;
                    case "after":
                        value = new Arg<string>("__after", true);
                        break;
                }

                arguments.Add(Expression.Constant(value, parameter.ParameterType));
            }

            return methodCall.Update(
                instance,
                arguments);
        }

        private object EvaluateValue(Expression expression)
        {
            if (expression is ConstantExpression c)
            {
                return c.Value;
            }
            else if (expression is LambdaExpression l)
            {
                var compiled = l.Compile();
                return compiled.DynamicInvoke();
            }
            else
            {
                var lambda = Expression.Lambda(expression);
                var compiled = lambda.Compile();
                return compiled.DynamicInvoke();
            }
        }

        private IEnumerable<ParameterExpression> RewriteParameters(IEnumerable<ParameterExpression> parameters)
        {
            var result = new List<ParameterExpression>();

            foreach (var parameter in parameters)
            {
                if (IsQueryableValue(parameter.Type))
                {
                    var rewritten = Expression.Parameter(typeof(JToken), parameter.Name);
                    var p = new LambdaParameter(
                        parameter,
                        rewritten,
                        syntax.Head);
                    lambdaParameters.Add(parameter, p);
                    result.Add(rewritten);
                }
                else
                {
                    result.Add(parameter);
                }
            }

            return result;
        }

        private Expression BookmarkAndVisit(Expression left)
        {
            using (syntax.Bookmark())
            {
                return Visit(left);
            }
        }

        private static Type GetEnumerableItemType(Type type)
        {
            var ti = type.GetTypeInfo();

            if (ti.IsGenericType && ti.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return ti.GenericTypeArguments[0];
            }
            else
            {
                throw new NotSupportedException("Not an IEnumerable<>.");
            }
        }

        private static Expression GetObject(Expression expression)
        {
            if (expression is MemberExpression member)
            {
                return member.Expression;
            }
            else if (expression is MethodCallExpression method)
            {
                return method.Object;
            }
            else
            {
                throw new NotImplementedException(
                    "Don't know how to get the object expression from " + expression);
            }
        }

        private static Type GetQueryableListItemType(Type type)
        {
            var ti = type.GetTypeInfo();

            if (ti.GetGenericTypeDefinition() == typeof(IQueryableList<>))
            {
                return ti.GenericTypeArguments[0];
            }
            else
            {
                throw new NotSupportedException("Not an IQueryableList<>.");
            }
        }

        private ISelectionSet GetSelectionSet(ParameterExpression parameter)
        {
            return lambdaParameters[parameter].SelectionSet;
        }

        private static bool ExpressionWasRewritten(Expression oldExpression, Expression newExpression)
        {
            return newExpression.Type == typeof(JToken) && oldExpression.Type != typeof(JToken);
        }

        private static bool IsNullConstant(Expression expression)
        {
            if (expression is ConstantExpression c)
            {
                return c.Value == null;
            }

            return false;
        }

        private static bool IsQueryableValue(Type type)
        {
            return typeof(IQueryableValue).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo());
        }

        private static bool IsQueryableValueMember(Expression expression)
        {
            var memberExpression = expression as MemberExpression;
            return memberExpression != null && IsQueryableValueMember(memberExpression.Member);
        }

        private static bool IsQueryableValueMember(MemberInfo member)
        {
            return IsQueryableValue(member.DeclaringType);
        }

        private static bool IsUnion(Type type)
        {
            return typeof(IUnion).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo());
        }

        private static bool IsUnionMember(MemberInfo member)
        {
            return member is PropertyInfo && IsUnion(member.DeclaringType);
        }

        private static FieldSelection PageInfoSelection()
        {
            var result = new FieldSelection("pageInfo", null);
            result.Selections.Add(new FieldSelection("hasNextPage", null));
            result.Selections.Add(new FieldSelection("endCursor", null));
            return result;
        }

        private class LambdaParameter
        {
            public LambdaParameter(
                ParameterExpression original,
                ParameterExpression rewritten,
                ISelectionSet selectionSet)
            {
                Original = original;
                Rewritten = rewritten;
                SelectionSet = selectionSet;
            }

            public ParameterExpression Original { get; }
            public ParameterExpression Rewritten { get; }
            public ISelectionSet SelectionSet { get; }
        }
    }
}
