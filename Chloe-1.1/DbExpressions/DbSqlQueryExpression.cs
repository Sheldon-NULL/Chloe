﻿using Chloe.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chloe.DbExpressions
{
    public class DbSqlQueryExpression : DbExpression
    {
        public DbSqlQueryExpression()
            : base(DbExpressionType.SqlQuery, UtilConstants.TypeOfVoid)
        {
            this.Columns = new List<DbColumnExpression>();
            this.GroupSegments = new List<DbExpression>();
            this.OrderSegments = new List<DbOrderSegmentExpression>();
        }
        public int? TakeCount { get; set; }
        public int? SkipCount { get; set; }
        public List<DbColumnExpression> Columns { get; private set; }
        public DbFromTableExpression Table { get; set; }
        public DbExpression Condition { get; set; }
        public List<DbExpression> GroupSegments { get; private set; }
        public DbExpression HavingCondition { get; set; }
        public List<DbOrderSegmentExpression> OrderSegments { get; private set; }

        public override T Accept<T>(DbExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }

        public string GenerateUniqueColumnAlias(string defaultAlias = "C")
        {
            string alias = defaultAlias;
            int i = 0;
            while (this.Columns.Any(a => a.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)))
            {
                alias = defaultAlias + i.ToString();
                i++;
            }

            return alias;
        }
    }
}
