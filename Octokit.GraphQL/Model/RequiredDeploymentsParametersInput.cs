namespace Octokit.GraphQL.Model
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Choose which environments must be successfully deployed to before branches can be merged into a branch that matches this rule.
    /// </summary>
    public class RequiredDeploymentsParametersInput
    {
        /// <summary>
        /// The environments that must be successfully deployed to before branches can be merged.
        /// </summary>
        public IEnumerable<string> RequiredDeploymentEnvironments { get; set; }
    }
}