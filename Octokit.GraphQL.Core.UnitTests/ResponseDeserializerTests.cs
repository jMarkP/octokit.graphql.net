﻿using System;
using System.Linq;
using System.Reflection;
using Octokit.GraphQL.Core.Builders;
using Octokit.GraphQL.Core.Deserializers;
using Octokit.GraphQL.Core.UnitTests.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Octokit.GraphQL.Core.UnitTests
{
    public class ResponseDeserializerTests
    {
        [Fact]
        public void Repository_Select_Single_Member()
        {
            var expression = new Query()
                .Repository("foo", "bar")
                .Select(x => x.Name);
            var data = @"{
  ""data"":{
    ""repository"":{
      ""name"": ""Hello World!""
    }
  }
}";

            var query = new QueryBuilder().Build(expression);
            var expectedType = expression.GetType().GetGenericArguments()[0];
            var result = query.Deserialize(data);

            Assert.IsType(expectedType, result);
            Assert.Equal("Hello World!", result);
        }

        [Fact]
        public void Repository_Select_Multiple_Members()
        {
            var expression = new Query()
                .Repository("foo", "bar")
                .Select(x => new { x.Name, x.Description });

            var data = @"{
  ""data"":{
    ""repository"":{
      ""name"": ""Hello World!"",
      ""description"": ""Goodbye cruel world""
    }
  }
}";

            var query = new QueryBuilder().Build(expression);
            var result = query.Deserialize(data);

            Assert.Equal("Hello World!", result.Name);
            Assert.Equal("Goodbye cruel world", result.Description);
        }

        [Fact]
        public void Repository_Select_Single_Member_With_Null_Conditional()
        {
            var expression = new Query()
                .Repository("foo", "bar")
                .Select(x => x.Name != null ? x.Name : "It's null!");
            var data = @"{
  ""data"":{
    ""repository"":{
      ""name"": null
    }
  }
}";

            var query = new QueryBuilder().Build(expression);
            var expectedType = expression.GetType().GetGenericArguments()[0];
            var result = query.Deserialize(data);

            Assert.IsType(expectedType, result);
            Assert.Equal("It's null!", result);
        }

        [Fact]
        public void Licenses_Select_Single_Member()
        {
            var expression = new Query()
                .Licenses
                .Select(x => x.Body);

            var data = @"{
  ""data"":{
    ""licenses"":[
      { ""body"": ""foo"" },
      { ""body"": ""bar"" }
    ]
  }
}";

            var query = new QueryBuilder().Build(expression);
            var result = query.Deserialize(data);

            Assert.Equal(new[] { "foo", "bar" }, result);
        }

        [Fact]
        public void Repository_Issue_Select_Multiple_Members()
        {
            var expression = new Query()
                .Repository("foo", "bar")
                .Issue(5)
                .Select(x => new { x.Title, x.Body });

            var data = @"{
    ""data"":{
        ""repository"": {
            ""issue"":{
              ""title"": ""Hello World!"",
              ""body"": ""Goodbye cruel world""
            }
        }
    }
}";

            var query = new QueryBuilder().Build(expression);
            var result = query.Deserialize(data);

            Assert.Equal("Hello World!", result.Title);
            Assert.Equal("Goodbye cruel world", result.Body);
        }

        [Fact]
        public void Repository_Issue_Select_Multiple_Members_As_Ctor_Parameters()
        {
            var expression = new Query()
                .Repository("foo", "bar")
                .Issue(5)
                .Select(x => new NamedClass(x.Title, x.Body));

            var data = @"{
    ""data"":{
        ""repository"": {
            ""issue"":{
              ""title"": ""Hello World!"",
              ""body"": ""Goodbye cruel world""
            }
        }
    }
}";

            var query = new QueryBuilder().Build(expression);
            var expectedType = expression.GetType().GetGenericArguments()[0];
            var result = query.Deserialize(data);

            Assert.IsType(expectedType, result);
            Assert.Equal("Hello World!", result.Name);
            Assert.Equal("Goodbye cruel world", result.Description);
        }

        [Fact]
        public void Repository_Issue_Select_Multiple_Members_To_Named_Class()
        {
            var expression = new Query()
                .Repository("foo", "bar")
                .Issue(5)
                .Select(x => new NamedClass
                {
                    Name = x.Title,
                    Description = x.Body,
                });

            var data = @"{
    ""data"":{
        ""repository"": {
            ""issue"":{
              ""name"": ""Hello World!"",
              ""description"": ""Goodbye cruel world""
            }
        }
    }
}";

            var query = new QueryBuilder().Build(expression);
            var expectedType = expression.GetType().GetGenericArguments()[0];
            var result = query.Deserialize(data);

            Assert.IsType(expectedType, result);
            Assert.Equal("Hello World!", result.Name);
            Assert.Equal("Goodbye cruel world", result.Description);
        }

        [Fact]
        public void License_Conditions_Nested_Selects()
        {
            var expression = new Query()
                .Licenses
                .Select(x => new
                {
                    x.Body,
                    Items = x.Conditions.Select(i => i.Description).ToList(),
                });

            var data = @"{
    ""data"":{
        ""licenses"": [{
            ""body"": ""foo"",
            ""items"": [
                { ""description"": ""item1"" },
                { ""description"": ""item2"" }
            ]
        }]
    }
}";

            var foo = JObject.Parse(data);

            var query = new QueryBuilder().Build(expression);
            var result = query.Deserialize(data).ToList();

            Assert.Equal("foo", result[0].Body);
            Assert.Equal(new[] { "item1", "item2" }, result[0].Items);
        }

        [Fact]
        public void Licenses_Field_Alias()
        {
            var expression = new Query()
                .Licenses
                .Select(x => new { Foo = x.Body });

            var data = @"{
    ""data"":{
        ""licenses"": [{
            ""foo"": ""123"",
        }]
    }
}";

            var foo = JObject.Parse(data);

            var query = new QueryBuilder().Build(expression);
            var result = query.Deserialize(data).ToList();

            Assert.Equal("123", result[0].Foo);
        }

        [Fact]
        public void Licenses_Conditions_Select_ToList()
        {
            var expression = new Query()
                .Licenses
                .Select(x => new
                {
                    x.Body,
                    Items = x.Conditions.Select(i => i.Description).ToList(),
                });

            var data = @"{
    ""data"":{
        ""licenses"": [{
            ""body"": ""foo"",
            ""items"": [
                { ""description"": ""item1"" },
                { ""description"": ""item2"" }
            ]
        }]
    }
}";

            var foo = JObject.Parse(data);

            var query = new QueryBuilder().Build(expression);
            var result = query.Deserialize(data).ToList();

            Assert.Equal("foo", result[0].Body);
            Assert.Equal(new[] { "item1", "item2" }, result[0].Items);
        }

        [Fact]
        public void Licenses_Conditions_Select_ToDictionary()
        {
            var expression = new Query()
                .Licenses
                .Select(x => new
                {
                    x.Body,
                    Items = x.Conditions.Select(i => new
                    {
                        i.Key,
                        i.Description,
                    }).ToDictionary(d => d.Key, d => d.Description),
                });

            var data = @"{
    ""data"":{
        ""licenses"": [{
            ""body"": ""foo"",
            ""items"": [
                { ""key"": ""item1"", ""description"": ""foo"" },
                { ""key"": ""item2"", ""description"": ""bar"" }
            ]
        }]
    }
}";

            var foo = JObject.Parse(data);

            var query = new QueryBuilder().Build(expression);
            var result = query.Deserialize(data).ToList();

            Assert.Equal("foo", result[0].Body);
            Assert.Equal(new[] { "item1", "item2" }, result[0].Items.Keys);
            Assert.Equal(new[] { "foo", "bar" }, result[0].Items.Values);
        }

        [Fact]
        public void Nodes_Repository_Fragment()
        {
            var expression = new Query()
                .Nodes(new[] { new ID("123") })
                .OfType<Repository>()
                .Select(x => new
                {
                    x.Name,
                    x.Description,
                });

            var data = @"{
    ""data"":{
        ""nodes"": [
            { 
                ""__typename"": ""Repository"",
                ""name"": ""foo"",
                ""description"": ""bar"" 
            },
            { 
                ""__typename"": ""Another"",
            }
        ]
    }
}";

            var foo = JObject.Parse(data);

            var query = new QueryBuilder().Build(expression);
            var result = query.Deserialize(data).ToList();

            Assert.Single(result);
            Assert.Equal("foo", result[0].Name);
            Assert.Equal("bar", result[0].Description);
        }

        [Fact]
        public void Repository_Issue_Single()
        {
            var expression = new Query()
                .Repository("foo", "bar")
                .Select(x => new
                {
                    Value = x.Issue(1).Select(y => new
                    {
                        y.Title,
                        y.Number,
                    }).Single()
                });

            var data = @"{
    ""data"":{
        ""repository"": {
            ""value"": {
                ""title"": ""foo"",
                ""number"": ""42""
            }
        }
    }
}";

            var foo = JObject.Parse(data);

            var query = new QueryBuilder().Build(expression);
            var result = query.Deserialize(data);

            Assert.Equal("foo", result.Value.Title);
            Assert.Equal(42, result.Value.Number);
        }

        [Fact]
        public void Repository_Issue_SingleOrDefault()
        {
            var expression = new Query()
                .Repository("foo", "bar")
                .Select(x => new
                {
                    Value = x.Issue(1).Select(y => new
                    {
                        y.Title,
                        y.Number,
                    }).SingleOrDefault()
                });

            var data = @"{
    ""data"":{
        ""repository"": {
            ""value"": {
                ""title"": ""foo"",
                ""number"": ""42""
            }
        }
    }
}";

            var foo = JObject.Parse(data);

            var query = new QueryBuilder().Build(expression);
            var result = query.Deserialize(data);

            Assert.Equal("foo", result.Value.Title);
            Assert.Equal(42, result.Value.Number);
        }

        private class NamedClass
        {
            public NamedClass()
            {
            }

            public NamedClass(string name, string description)
            {
                Name = name;
                Description = description;
            }

            public string Name { get; set; }
            public string Description { get; set; }
        }
    }
}
