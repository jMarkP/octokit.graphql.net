namespace Octokit.GraphQL.Model
{
    using System;
    using System.Collections.Generic;
    using System.Linq.Expressions;
    using Octokit.GraphQL.Core;
    using Octokit.GraphQL.Core.Builders;

    /// <summary>
    /// Autogenerated return type of UnresolveReviewThread
    /// </summary>
    public class UnresolveReviewThreadPayload : QueryableValue<UnresolveReviewThreadPayload>
    {
        internal UnresolveReviewThreadPayload(Expression expression) : base(expression)
        {
        }

        /// <summary>
        /// A unique identifier for the client performing the mutation.
        /// </summary>
        public string ClientMutationId { get; }

        /// <summary>
        /// The thread to resolve.
        /// </summary>
        public PullRequestReviewThread Thread => this.CreateProperty(x => x.Thread, Octokit.GraphQL.Model.PullRequestReviewThread.Create);

        internal static UnresolveReviewThreadPayload Create(Expression expression)
        {
            return new UnresolveReviewThreadPayload(expression);
        }
    }
}