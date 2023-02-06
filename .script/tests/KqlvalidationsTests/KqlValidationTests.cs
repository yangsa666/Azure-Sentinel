﻿using System.Collections.Generic;
using Microsoft.Azure.Sentinel.KustoServices.Contract;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.Serialization;
using Microsoft.Azure.Sentinel.KustoServices.Implementation;
using Kqlvalidations.Tests.FunctionSchemasLoaders;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema.Generation;

namespace Kqlvalidations.Tests
{
    public class KqlValidationTests
    {
        private readonly IKqlQueryAnalyzer _queryValidator;
        private const int TestFolderDepth = 3;

        public KqlValidationTests()
        {
            _queryValidator = new KqlQueryAnalyzerBuilder()
               .WithSentinelDefaultTablesAndFunctionsSchemas()
               .WithCustomTableSchemasLoader(new CustomJsonDirectoryTablesLoader(Path.Combine(Utils.GetTestDirectory(TestFolderDepth), "CustomTables")))
               .WithCustomFunctionSchemasLoader(new CustomJsonDirectoryFunctionsLoader(Path.Combine(Utils.GetTestDirectory(TestFolderDepth), "CustomFunctions")))
               .WithCustomFunctionSchemasLoader(new ParsersCustomJsonDirectoryFunctionsLoader(Path.Combine(Utils.GetTestDirectory(TestFolderDepth), "CustomFunctions")))
               .WithCustomFunctionSchemasLoader(new CommonFunctionsLoader())
               .Build();
        }

        // We pass File name to test because in the result file we want to show an informative name for the test
        [Theory]
        [ClassData(typeof(WorkbookFilesTestData))]
        public void Validate_Workbooks_HaveValidKql(string fileName, string encodedFilePath)
        {
            var workbookQueries = GetQueriesFromWorkbook(encodedFilePath);

            //loop through workbookQueries and using ValidateKqlForWorkbooks method
            foreach (var query in workbookQueries)
            {
                ValidateKqlForWorkbooks(fileName, query);
            }


        }

        // We pass File name to test because in the result file we want to show an informative name for the test
        [Theory]
        [ClassData(typeof(DataConnectorFilesTestData))]
        public void Validate_DataConnectors_HaveValidKql(string fileName, string encodedFilePath)
        {
            var dataConnector = ReadAndDeserializeDataConnectorJson(encodedFilePath);
            var id = (string)dataConnector.Id;
            //we ignore known issues
            if (ShouldSkipTemplateValidation(id))
            {
                return;
            }
            foreach (var connectivityCriteria in dataConnector.ConnectivityCriterias)
            {
                foreach (var queryStr in connectivityCriteria.Value)
                {
                    ValidateKql(id, queryStr);
                }
            }

            foreach (var sampleQuery in dataConnector.SampleQueries)
            {
                ValidateKql(id, sampleQuery.Query);
            }

            foreach (var graphQuery in dataConnector.GraphQueries)
            {
                ValidateKql(id, graphQuery.BaseQuery);
            }

            foreach (var datatype in dataConnector.DataTypes)
            {
                ValidateKql(id, datatype.LastDataReceivedQuery);
            }
        }


        // We pass File name to test because in the result file we want to show an informative name for the test
        [Theory]
        [ClassData(typeof(DetectionsYamlFilesTestData))]
        public void Validate_DetectionQueries_HaveValidKql(string fileName, string encodedFilePath)
        {
            var res = ReadAndDeserializeYaml(encodedFilePath);
            var id = (string)res["id"];

            //we ignore known issues. We also ignore templates that are not in the skipped templates list.
            if (ShouldSkipTemplateValidation(id))
            {
                return;
            }

            var queryStr = (string)res["query"];
            ValidateKql(id, queryStr);
        }


        // We pass File name to test because in the result file we want to show an informative name for the test
        [Theory]
        [ClassData(typeof(HuntingQueriesYamlFilesTestData))]
        public void Validate_HuntingQueries_SkippedTemplatesDoNotHaveValidKql(string fileName, string encodedFilePath)
        {
            var res = ReadAndDeserializeYaml(encodedFilePath);
            var id = (string)res["id"];

            //Templates that are in the skipped templates should not pass the validation (if they pass, why skip?)
            if (ShouldSkipTemplateValidation(id) && res.ContainsKey("query"))
            {
                var queryStr = (string)res["query"];
                var validationRes = _queryValidator.ValidateSyntax(queryStr);
                Assert.False(validationRes.IsValid, $"Template Id:{id} is valid but it is in the skipped validation templates. Please remove it from the templates that are skipped since it is valid.");
            }

        }

        // We pass File name to test because in the result file we want to show an informative name for the test
        [Theory]
        [ClassData(typeof(DetectionsYamlFilesTestData))]
        public void Validate_DetectionQueries_SkippedTemplatesDoNotHaveValidKql(string fileName, string encodedFilePath)
        {
            var res = ReadAndDeserializeYaml(encodedFilePath);
            var id = (string)res["id"];

            //Templates that are in the skipped templates should not pass the validation (if they pass, why skip?)
            if (ShouldSkipTemplateValidation(id) && res.ContainsKey("query"))
            {
                var queryStr = (string)res["query"];
                var validationRes = _queryValidator.ValidateSyntax(queryStr);
                Assert.False(validationRes.IsValid, $"Template Id:{id} is valid but it is in the skipped validation templates. Please remove it from the templates that are skipped since it is valid.");
            }

        }

        // // We pass File name to test because in the result file we want to show an informative name for the test
        // [Theory]
        // [ClassData(typeof(InsightsYamlFilesTestData))]
        // public void Validate_InsightsQueries_HaveValidKqlBaseQuery(string fileName, string encodedFilePath)
        // {
        //     var res = ReadAndDeserializeYaml(encodedFilePath);
        //     var queryStr =  (string) res["BaseQuery"];
        //     
        //     ValidateKql(fileProp.FileName, queryStr);
        // }

        [Theory]
        [ClassData(typeof(ExplorationQueriesYamlFilesTestData))]
        public void Validate_ExplorationQueries_HaveValidKql(string fileName, string encodedFilePath)
        {
            var res = ReadAndDeserializeYaml(encodedFilePath);
            var id = (string)res["Id"];

            //we ignore known issues
            if (ShouldSkipTemplateValidation(id))
            {
                return;
            }

            var queryStr = (string)res["query"];
            ValidateKql(id, queryStr);
        }

        [Theory]
        [ClassData(typeof(ExplorationQueriesYamlFilesTestData))]
        public void Validate_ExplorationQueries_SkippedTemplatesDoNotHaveValidKql(string fileName, string encodedFilePath)
        {
            var res = ReadAndDeserializeYaml(encodedFilePath);
            var id = (string)res["Id"];

            //Templates that are in the skipped templates should not pass the validation (if they pass, why skip?)
            if (ShouldSkipTemplateValidation(id) && res.ContainsKey("query"))
            {
                var queryStr = (string)res["query"];
                var validationRes = _queryValidator.ValidateSyntax(queryStr);
                Assert.False(validationRes.IsValid, $"Template Id:{id} is valid but it is in the skipped validation templates. Please remove it from the templates that are skipped since it is valid.");
            }

        }

        // We pass File name to test because in the result file we want to show an informative name for the test
        [Theory]
        [ClassData(typeof(ParsersYamlFilesTestData))]
        public void Validate_ParsersFunctions_HaveValidKql(string fileName, string encodedFilePath)
        {
            Dictionary<object, object> yaml = ReadAndDeserializeYaml(encodedFilePath);
            var queryParamsAsLetStatements = GenerateFunctionParametersAsLetStatements(yaml);

            //Ignore known issues
            yaml.TryGetValue("Id", out object id);
            if (id != null && ShouldSkipTemplateValidation((string)yaml["Id"]))
            {
                return;
            }

            var queryStr = queryParamsAsLetStatements + (string)yaml["ParserQuery"];
            var parserName = (string)yaml["ParserName"];
            ValidateKql(parserName, queryStr, false);
        }

        // We pass File name to test because in the result file we want to show an informative name for the test
        [Theory]
        [ClassData(typeof(CommonFunctionsYamlFilesTestData))]
        public void Validate_CommonFunctions_HaveValidKql(string fileName, string encodedFilePath)
        {
            Dictionary<object, object> yaml = ReadAndDeserializeYaml(encodedFilePath);
            var queryParamsAsLetStatements = GenerateFunctionParametersAsLetStatements(yaml, "FunctionParams");

            //Ignore known issues
            yaml.TryGetValue("Id", out object id);
            if (id != null && ShouldSkipTemplateValidation((string)id))
            {
                return;
            }

            var queryStr = queryParamsAsLetStatements + (string)yaml["FunctionQuery"];            
            var parserName = (string)yaml["EquivalentBuiltInFunction"];
            ValidateKql(parserName, queryStr, false);
        }

        private void ValidateKql(string id, string queryStr, bool ignoreNoTabularExpressionError = true)
        {
            
            // The KQL validation ignores no tabular expression error. For instance, "let x = table;" is considered a valid query.
            // Add "| count" at the end of the query, to fail queries without tabular expressions.
            if (!ignoreNoTabularExpressionError) {
                queryStr += " | count";
            }

            var validationResult = _queryValidator.ValidateSyntax(queryStr);
            var firstErrorLocation = (Line: 0, Col: 0);
            if (!validationResult.IsValid)
            {
                firstErrorLocation = GetLocationInQuery(queryStr, validationResult.Diagnostics.First(d => d.Severity == "Error").Start);
            }

            var listOfDiagnostics = validationResult.Diagnostics;

            bool isQueryValid = !(from p in listOfDiagnostics
                                  where !p.Message.Contains("_GetWatchlist") //We do not validate the getWatchList, since the result schema is not known
                                  select p).Any();


            Assert.True(
                isQueryValid,
                isQueryValid
                    ? string.Empty
                    : @$"Template Id: {id} is not valid in Line: {firstErrorLocation.Line} col: {firstErrorLocation.Col}
                    Errors: {validationResult.Diagnostics.Select(d => d.ToString()).ToList().Aggregate((s1, s2) => s1 + "," + s2)}");
        }

        private void ValidateKqlForWorkbooks(string filename,string queryStr)
        {
            var validationResult = _queryValidator.ValidateSyntax(queryStr);
            var firstErrorLocation = (Line: 0, Col: 0);
            if (!validationResult.IsValid)
            {
                firstErrorLocation = GetLocationInQuery(queryStr, validationResult.Diagnostics.First(d => d.Severity == "Error").Start);
            }

            var listOfDiagnostics = validationResult.Diagnostics;

            bool isQueryValid = !(from p in listOfDiagnostics
                                  where !p.Message.Contains("_GetWatchlist") //We do not validate the getWatchList, since the result schema is not known
                                  select p).Any();


            Assert.True(
                isQueryValid,
                isQueryValid
                    ? string.Empty
                    : @$"File: {filename} is not valid in Line: {firstErrorLocation.Line} col: {firstErrorLocation.Col}
                    Errors: {validationResult.Diagnostics.Select(d => d.ToString()).ToList().Aggregate((s1, s2) => s1 + "," + s2)}");
        }

        private Dictionary<object, object> ReadAndDeserializeYaml(string encodedFilePath)
        {

            var yaml = File.ReadAllText(Utils.DecodeBase64(encodedFilePath));
            var deserializer = new DeserializerBuilder().Build();
            return deserializer.Deserialize<dynamic>(yaml);
        }

        private DataConnectorSchema ReadAndDeserializeDataConnectorJson(string encodedFilePath)
        {
            var jsonString = File.ReadAllText(Utils.DecodeBase64(encodedFilePath));
            DataConnectorSchema dataConnectorObject = JsonConvert.DeserializeObject<DataConnectorSchema>(jsonString);
            return dataConnectorObject;
        }


        private List<string> GetQueriesFromWorkbook(string encodedFilePath)
        {
            var jsonString = File.ReadAllText(Utils.DecodeBase64(encodedFilePath));
            List<string> queries = new List<string>();
            var data = JsonConvert.DeserializeObject<dynamic>(jsonString);
            if (data.items!=null)
            {
                GetQueriesFromItems(queries, data.items);
            }
            return queries;
        }

        private void GetQueriesFromItems(List<string> queries, dynamic items)
        {
            foreach (var item in items)
            {
                var content = item.content;
                var json = JsonConvert.SerializeObject(content);
                Content contentObject = JsonConvert.DeserializeObject<Content>(json);
                if (!string.IsNullOrEmpty(contentObject.Query))
                    queries.Add(contentObject.Query);
                if (contentObject.items!=null && contentObject.items.Count>0)
                {
                    GetQueriesFromItems(queries, contentObject.items);
                }
            }
        }

        private bool ShouldSkipTemplateValidation(string templateId)
        {
            return TemplatesToSkipValidationReader.WhiteListTemplates
                .Where(template => template.id == templateId)
                .Where(template => !string.IsNullOrWhiteSpace(template.validationFailReason))
                .Where(template => !string.IsNullOrWhiteSpace(template.templateName))
                .Any();
        }

        private (int Line, int Col) GetLocationInQuery(string queryStr, int pos)
        {
            var lines = Regex.Split(queryStr, "\n");
            var curlineIndex = 0;
            var curPos = 0;

            while (lines.Length > curlineIndex && pos > curPos + lines[curlineIndex].Length + 1)
            {
                curPos += lines[curlineIndex].Length + 1;
                curlineIndex++;
            }
            var col = (pos - curPos + 1);
            return (curlineIndex + 1, col);
        }

        /// <summary>
        /// Generate a string of function parameters as let statements.
        /// </summary>
        /// <param name="yaml">The parser's yaml file</param>
        /// <returns>The function parameters as let statements</returns>
        private string GenerateFunctionParametersAsLetStatements(Dictionary<object, object> yaml, string paramsKey = "ParserParams")
        {
            if (yaml.TryGetValue(paramsKey, out object parserParamsObject))
            {
                var parserParams = (List<object>)parserParamsObject;
                return string.Join(Environment.NewLine, parserParams.Select(GenerateParamaterAsLetStatement).ToList());
            }
            return "";
        }

        /// <summary>
        /// Convert function parameter to a let statement with the format 'let <parameterName>= <defaultValue>;
        /// </summary>
        /// <param name="parameter">A function parameter as an object</param>
        /// <returns>A function parameter as a let statement</returns>
        private string GenerateParamaterAsLetStatement(object parameter)
        {
            var dictionary = (IReadOnlyDictionary<object, object>)parameter;
            string name = (string)dictionary["Name"];
            string type = (string)dictionary["Type"];
            string defaultValue = ((string)dictionary.GetValueOrDefault("Default")) ?? TypesDatabase.TypeToDefaultValueMapping.GetValueOrDefault(type);
            return $"let {name}= {(type == "string" ? $"'{defaultValue}'" : defaultValue)};";
        }
    }

}

