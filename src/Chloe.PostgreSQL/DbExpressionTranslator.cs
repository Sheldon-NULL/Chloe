﻿using Chloe.Core;
using Chloe.DbExpressions;
using Chloe.Infrastructure;

namespace Chloe.PostgreSQL
{
    class DbExpressionTranslator : IDbExpressionTranslator
    {
        PostgreSQLContextProvider ContextProvider { get; set; }

        public DbExpressionTranslator(PostgreSQLContextProvider contextProvider)
        {
            this.ContextProvider = contextProvider;
        }

        public DbCommandInfo Translate(DbExpression expression, List<object> variables)
        {
            PostgreSQLSqlGeneratorOptions options = this.CreateOptions();
            SqlGenerator generator = new SqlGenerator(options);
            expression = EvaluableDbExpressionTransformer.Transform(expression, variables);
            expression.Accept(generator);

            DbCommandInfo dbCommandInfo = new DbCommandInfo();
            dbCommandInfo.Parameters = generator.Parameters;
            dbCommandInfo.CommandText = generator.SqlBuilder.ToSql();

            return dbCommandInfo;
        }

        PostgreSQLSqlGeneratorOptions CreateOptions()
        {
            var options = new PostgreSQLSqlGeneratorOptions()
            {
                LeftQuoteChar = UtilConstants.LeftQuoteChar,
                RightQuoteChar = UtilConstants.RightQuoteChar,
                MaxInItems = this.ContextProvider.Options.MaxInItems,
                ConvertToLowercase = this.ContextProvider.Options.ConvertToLowercase
            };

            return options;
        }
    }
}
