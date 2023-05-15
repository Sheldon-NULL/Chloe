﻿using Chloe.Annotations;
using Chloe.DbExpressions;
using Chloe.InternalExtensions;
using Chloe.Reflection;
using Chloe.Visitors;
using System.Reflection;

namespace Chloe.RDBMS
{
    public abstract class SqlGeneratorBase : DbExpressionVisitor<DbExpression>
    {
        ISqlBuilder _sqlBuilder = new SqlBuilder();

        protected SqlGeneratorBase()
        {

        }

        public ISqlBuilder SqlBuilder { get { return this._sqlBuilder; } }


        protected abstract string LeftQuoteChar { get; }
        protected abstract string RightQuoteChar { get; }
        protected abstract Dictionary<string, IMethodHandler> MethodHandlers { get; }
        protected abstract Dictionary<string, Action<DbAggregateExpression, SqlGeneratorBase>> AggregateHandlers { get; }
        protected abstract Dictionary<MethodInfo, Action<DbBinaryExpression, SqlGeneratorBase>> BinaryWithMethodHandlers { get; }

        public override DbExpression Visit(DbEqualExpression exp)
        {
            DbExpression left = exp.Left;
            DbExpression right = exp.Right;

            left = DbExpressionExtension.StripInvalidConvert(left);
            right = DbExpressionExtension.StripInvalidConvert(right);

            MethodInfo method_Sql_IsEqual = PublicConstants.MethodInfo_Sql_IsEqual.MakeGenericMethod(left.Type);

            /* Sql.IsEqual(left, right) */
            DbMethodCallExpression left_equals_right = DbExpression.MethodCall(null, method_Sql_IsEqual, new List<DbExpression>(2) { left, right });

            if (right.NodeType == DbExpressionType.Parameter || right.NodeType == DbExpressionType.Constant || left.NodeType == DbExpressionType.Parameter || left.NodeType == DbExpressionType.Constant || right.NodeType == DbExpressionType.SubQuery || left.NodeType == DbExpressionType.SubQuery || !left.Type.CanNull() || !right.Type.CanNull())
            {
                /*
                 * a.Name == name --> a.Name == name
                 * a.Id == (select top 1 T.Id from T) --> a.Id == (select top 1 T.Id from T)
                 * 对于上述查询，我们不考虑 null
                 */

                left_equals_right.Accept(this);
                return exp;
            }


            /*
             * a.Name == a.XName --> a.Name == a.XName or (a.Name is null and a.XName is null)
             */

            /* Sql.IsEqual(left, null) */
            var left_is_null = DbExpression.MethodCall(null, method_Sql_IsEqual, new List<DbExpression>(2) { left, DbExpression.Constant(null, left.Type) });

            /* Sql.IsEqual(right, null) */
            var right_is_null = DbExpression.MethodCall(null, method_Sql_IsEqual, new List<DbExpression>(2) { right, DbExpression.Constant(null, right.Type) });

            /* Sql.IsEqual(left, null) && Sql.IsEqual(right, null) */
            var left_is_null_and_right_is_null = DbExpression.And(left_is_null, right_is_null);

            /* Sql.IsEqual(left, right) || (Sql.IsEqual(left, null) && Sql.IsEqual(right, null)) */
            var left_equals_right_or_left_is_null_and_right_is_null = DbExpression.Or(left_equals_right, left_is_null_and_right_is_null);

            left_equals_right_or_left_is_null_and_right_is_null.Accept(this);

            return exp;
        }

        public override DbExpression Visit(DbNotEqualExpression exp)
        {
            DbExpression left = exp.Left;
            DbExpression right = exp.Right;

            left = DbExpressionExtension.StripInvalidConvert(left);
            right = DbExpressionExtension.StripInvalidConvert(right);

            MethodInfo method_Sql_IsNotEqual = PublicConstants.MethodInfo_Sql_IsNotEqual.MakeGenericMethod(left.Type);

            /* Sql.IsNotEqual(left, right) */
            DbMethodCallExpression left_not_equals_right = DbExpression.MethodCall(null, method_Sql_IsNotEqual, new List<DbExpression>(2) { left, right });

            //明确 left right 其中一边一定为 null
            if (DbExpressionExtension.AffirmExpressionRetValueIsNull(right) || DbExpressionExtension.AffirmExpressionRetValueIsNull(left))
            {
                /*
                 * a.Name != null --> a.Name != null
                 */

                left_not_equals_right.Accept(this);
                return exp;
            }

            if (right.NodeType == DbExpressionType.SubQuery || left.NodeType == DbExpressionType.SubQuery)
            {
                /*
                 * a.Id != (select top 1 T.Id from T) --> a.Id <> (select top 1 T.Id from T)，对于这种查询，我们不考虑 null
                 */

                left_not_equals_right.Accept(this);
                return exp;
            }

            MethodInfo method_Sql_IsEqual = PublicConstants.MethodInfo_Sql_IsEqual.MakeGenericMethod(left.Type);

            if (left.NodeType == DbExpressionType.Parameter || left.NodeType == DbExpressionType.Constant)
            {
                var t = right;
                right = left;
                left = t;
            }
            if (right.NodeType == DbExpressionType.Parameter || right.NodeType == DbExpressionType.Constant)
            {
                /*
                 * 走到这说明 name 不可能为 null
                 * a.Name == name --> a.Name != name or a.Name is null
                 */

                if (left.NodeType != DbExpressionType.Parameter && left.NodeType != DbExpressionType.Constant)
                {
                    /*
                     * a.Name != name --> a.Name <> name or a.Name is null
                     */

                    /* Sql.IsEqual(left, null) */
                    var left_is_null1 = DbExpression.MethodCall(null, method_Sql_IsEqual, new List<DbExpression>(2) { left, DbExpression.Constant(null, left.Type) });

                    /* Sql.IsNotEqual(left, right) || Sql.IsEqual(left, null) */
                    var left_not_equals_right_or_left_is_null = DbExpression.Or(left_not_equals_right, left_is_null1);
                    left_not_equals_right_or_left_is_null.Accept(this);
                }
                else
                {
                    /*
                     * name != name1 --> name <> name，其中 name 和 name1 都为变量且都不可能为 null
                     */

                    left_not_equals_right.Accept(this);
                }

                return exp;
            }


            /*
             * a.Name != a.XName --> a.Name <> a.XName or (a.Name is null and a.XName is not null) or (a.Name is not null and a.XName is null)
             * ## a.Name != a.XName 不能翻译成：not (a.Name == a.XName or (a.Name is null and a.XName is null))，因为数据库里的 not 有时候并非真正意义上的“取反”！
             * 当 a.Name 或者 a.XName 其中一个字段有为 NULL，另一个字段有值时，会查不出此条数据 ##
             */

            DbConstantExpression null_Constant = DbExpression.Constant(null, left.Type);

            /* Sql.IsEqual(left, null) */
            var left_is_null = DbExpression.MethodCall(null, method_Sql_IsEqual, new List<DbExpression>(2) { left, null_Constant });
            /* Sql.IsNotEqual(left, null) */
            var left_is_not_null = DbExpression.MethodCall(null, method_Sql_IsNotEqual, new List<DbExpression>(2) { left, null_Constant });

            /* Sql.IsEqual(right, null) */
            var right_is_null = DbExpression.MethodCall(null, method_Sql_IsEqual, new List<DbExpression>(2) { right, null_Constant });
            /* Sql.IsNotEqual(right, null) */
            var right_is_not_null = DbExpression.MethodCall(null, method_Sql_IsNotEqual, new List<DbExpression>(2) { right, null_Constant });

            /* Sql.IsEqual(left, null) && Sql.IsNotEqual(right, null) */
            var left_is_null_and_right_is_not_null = DbExpression.And(left_is_null, right_is_not_null);

            /* Sql.IsNotEqual(left, null) && Sql.IsEqual(right, null) */
            var left_is_not_null_and_right_is_null = DbExpression.And(left_is_not_null, right_is_null);

            /* (Sql.IsEqual(left, null) && Sql.IsNotEqual(right, null)) || (Sql.IsNotEqual(left, null) && Sql.IsEqual(right, null)) */
            var left_is_null_and_right_is_not_null_or_left_is_not_null_and_right_is_null = DbExpression.Or(left_is_null_and_right_is_not_null, left_is_not_null_and_right_is_null);

            /* Sql.IsNotEqual(left, right) || (Sql.IsEqual(left, null) && Sql.IsNotEqual(right, null)) || (Sql.IsNotEqual(left, null) && Sql.IsEqual(right, null)) */
            var e = DbExpression.Or(left_not_equals_right, left_is_null_and_right_is_not_null_or_left_is_not_null_and_right_is_null);

            e.Accept(this);

            return exp;
        }

        public override DbExpression Visit(DbNotExpression exp)
        {
            this.SqlBuilder.Append("NOT ");
            this.SqlBuilder.Append("(");
            exp.Operand.Accept(this);
            this.SqlBuilder.Append(")");

            return exp;
        }

        public override DbExpression Visit(DbBitAndExpression exp)
        {
            Stack<DbExpression> operands = PublicHelper.GatherBinaryExpressionOperand(exp);
            this.ConcatOperands(operands, " & ");

            return exp;
        }
        public override DbExpression Visit(DbAndExpression exp)
        {
            Stack<DbExpression> operands = PublicHelper.GatherBinaryExpressionOperand(exp);
            this.ConcatOperands(operands, " AND ");

            return exp;
        }
        public override DbExpression Visit(DbBitOrExpression exp)
        {
            Stack<DbExpression> operands = PublicHelper.GatherBinaryExpressionOperand(exp);
            this.ConcatOperands(operands, " | ");

            return exp;
        }
        public override DbExpression Visit(DbOrExpression exp)
        {
            Stack<DbExpression> operands = PublicHelper.GatherBinaryExpressionOperand(exp);
            this.ConcatOperands(operands, " OR ");

            return exp;
        }

        // +
        public override DbExpression Visit(DbAddExpression exp)
        {
            MethodInfo method = exp.Method;
            if (method != null)
            {
                Action<DbBinaryExpression, SqlGeneratorBase> handler;
                if (this.BinaryWithMethodHandlers.TryGetValue(method, out handler))
                {
                    handler(exp, this);
                    return exp;
                }
            }

            Stack<DbExpression> operands = PublicHelper.GatherBinaryExpressionOperand(exp);
            this.ConcatOperands(operands, " + ");

            return exp;
        }
        // -
        public override DbExpression Visit(DbSubtractExpression exp)
        {
            Stack<DbExpression> operands = PublicHelper.GatherBinaryExpressionOperand(exp);
            this.ConcatOperands(operands, " - ");

            return exp;
        }
        // *
        public override DbExpression Visit(DbMultiplyExpression exp)
        {
            Stack<DbExpression> operands = PublicHelper.GatherBinaryExpressionOperand(exp);
            this.ConcatOperands(operands, " * ");

            return exp;
        }
        // /
        public override DbExpression Visit(DbDivideExpression exp)
        {
            Stack<DbExpression> operands = PublicHelper.GatherBinaryExpressionOperand(exp);
            this.ConcatOperands(operands, " / ");

            return exp;
        }
        // %
        public override DbExpression Visit(DbModuloExpression exp)
        {
            Stack<DbExpression> operands = PublicHelper.GatherBinaryExpressionOperand(exp);
            this.ConcatOperands(operands, " % ");

            return exp;
        }


        public override DbExpression Visit(DbNegateExpression exp)
        {
            this.SqlBuilder.Append("(");

            this.SqlBuilder.Append("-");
            exp.Operand.Accept(this);

            this.SqlBuilder.Append(")");
            return exp;
        }
        // <
        public override DbExpression Visit(DbLessThanExpression exp)
        {
            var amendResult = PublicHelper.AmendExpDbInfo(exp.Left, exp.Right);

            amendResult.Left.Accept(this);
            this.SqlBuilder.Append(" < ");
            amendResult.Right.Accept(this);

            return exp;
        }
        // <=
        public override DbExpression Visit(DbLessThanOrEqualExpression exp)
        {
            var amendResult = PublicHelper.AmendExpDbInfo(exp.Left, exp.Right);

            amendResult.Left.Accept(this);
            this.SqlBuilder.Append(" <= ");
            amendResult.Right.Accept(this);

            return exp;
        }
        // >
        public override DbExpression Visit(DbGreaterThanExpression exp)
        {
            var amendResult = PublicHelper.AmendExpDbInfo(exp.Left, exp.Right);

            amendResult.Left.Accept(this);
            this.SqlBuilder.Append(" > ");
            amendResult.Right.Accept(this);

            return exp;
        }
        // >=
        public override DbExpression Visit(DbGreaterThanOrEqualExpression exp)
        {
            var amendResult = PublicHelper.AmendExpDbInfo(exp.Left, exp.Right);

            amendResult.Left.Accept(this);
            this.SqlBuilder.Append(" >= ");
            amendResult.Right.Accept(this);

            return exp;
        }

        public override DbExpression Visit(DbAggregateExpression exp)
        {
            Action<DbAggregateExpression, SqlGeneratorBase> aggregateHandler;
            if (!AggregateHandlers.TryGetValue(exp.Method.Name, out aggregateHandler))
            {
                throw PublicHelper.MakeNotSupportedMethodException(exp.Method);
            }

            aggregateHandler(exp, this);
            return exp;
        }

        public override DbExpression Visit(DbTableExpression exp)
        {
            this.AppendTable(exp.Table);
            return exp;
        }
        public override DbExpression Visit(DbColumnAccessExpression exp)
        {
            this.QuoteName(exp.Table.Name);
            this.SqlBuilder.Append(".");
            this.QuoteName(exp.Column.Name);

            return exp;
        }
        public override DbExpression Visit(DbFromTableExpression exp)
        {
            this.AppendTableSegment(exp.Table);
            this.VisitDbJoinTableExpressions(exp.JoinTables);

            return exp;
        }


        public override DbExpression Visit(DbJoinTableExpression exp)
        {
            DbJoinTableExpression joinTablePart = exp;
            string joinString = null;

            if (joinTablePart.JoinType == DbJoinType.InnerJoin)
            {
                joinString = " INNER JOIN ";
            }
            else if (joinTablePart.JoinType == DbJoinType.LeftJoin)
            {
                joinString = " LEFT JOIN ";
            }
            else if (joinTablePart.JoinType == DbJoinType.RightJoin)
            {
                joinString = " RIGHT JOIN ";
            }
            else if (joinTablePart.JoinType == DbJoinType.FullJoin)
            {
                joinString = " FULL JOIN ";
            }
            else
                throw new NotSupportedException("JoinType: " + joinTablePart.JoinType);

            this.SqlBuilder.Append(joinString);
            this.AppendTableSegment(joinTablePart.Table);
            this.SqlBuilder.Append(" ON ");
            JoinConditionExpressionTransformer.Transform(joinTablePart.Condition).Accept(this);
            this.VisitDbJoinTableExpressions(joinTablePart.JoinTables);

            return exp;
        }

        public override DbExpression Visit(DbSubQueryExpression exp)
        {
            this.SqlBuilder.Append("(");
            exp.SqlQuery.Accept(this);
            this.SqlBuilder.Append(")");

            return exp;
        }

        public override DbExpression Visit(DbInsertExpression exp)
        {
            this.SqlBuilder.Append("INSERT INTO ");
            this.AppendTable(exp.Table);
            this.SqlBuilder.Append("(");

            string separator = "";
            foreach (var item in exp.InsertColumns)
            {
                this.SqlBuilder.Append(separator);
                this.QuoteName(item.Key.Name);
                separator = ",";
            }

            this.SqlBuilder.Append(")");

            this.SqlBuilder.Append(" VALUES(");
            separator = "";
            foreach (var item in exp.InsertColumns)
            {
                this.SqlBuilder.Append(separator);

                DbExpression valExp = DbExpressionExtension.StripInvalidConvert(item.Value);
                PublicHelper.AmendDbInfo(item.Key, valExp);
                valExp.Accept(this);
                separator = ",";
            }

            this.SqlBuilder.Append(")");

            return exp;
        }

        public override DbExpression Visit(DbUpdateExpression exp)
        {
            this.SqlBuilder.Append("UPDATE ");
            this.AppendTable(exp.Table);
            this.SqlBuilder.Append(" SET ");

            string separator = "";
            foreach (var item in exp.UpdateColumns)
            {
                this.SqlBuilder.Append(separator);
                this.QuoteName(item.Key.Name);
                this.SqlBuilder.Append("=");

                DbExpression valExp = item.Value.StripInvalidConvert();
                PublicHelper.AmendDbInfo(item.Key, valExp);
                valExp.Accept(this);

                separator = ",";
            }

            this.BuildWhereState(exp.Condition);

            return exp;
        }

        public override DbExpression Visit(DbDeleteExpression exp)
        {
            this.SqlBuilder.Append("DELETE FROM ");
            this.AppendTable(exp.Table);
            this.BuildWhereState(exp.Condition);

            return exp;
        }

        public override DbExpression Visit(DbExistsExpression exp)
        {
            this.SqlBuilder.Append("Exists ");

            DbSqlQueryExpression rawSqlQuery = exp.SqlQuery;
            DbSqlQueryExpression sqlQuery = new DbSqlQueryExpression()
            {
                TakeCount = rawSqlQuery.TakeCount,
                SkipCount = rawSqlQuery.SkipCount,
                Table = rawSqlQuery.Table,
                Condition = rawSqlQuery.Condition,
                HavingCondition = rawSqlQuery.HavingCondition
            };

            sqlQuery.GroupSegments.AddRange(rawSqlQuery.GroupSegments);

            DbColumnSegment columnSegment = new DbColumnSegment(DbExpression.Parameter("1"), "C");
            sqlQuery.ColumnSegments.Add(columnSegment);

            DbSubQueryExpression subQuery = new DbSubQueryExpression(sqlQuery);
            return subQuery.Accept(this);
        }

        public override DbExpression Visit(DbCaseWhenExpression exp)
        {
            this.SqlBuilder.Append("CASE");
            foreach (var whenThen in exp.WhenThenPairs)
            {
                this.SqlBuilder.Append(" WHEN ");
                whenThen.When.Accept(this);
                this.SqlBuilder.Append(" THEN ");
                whenThen.Then.Accept(this);
            }

            this.SqlBuilder.Append(" ELSE ");
            exp.Else.Accept(this);
            this.SqlBuilder.Append(" END");

            return exp;
        }

        public override DbExpression Visit(DbMethodCallExpression exp)
        {
            IMethodHandler methodHandler;
            if (this.MethodHandlers.TryGetValue(exp.Method.Name, out methodHandler))
            {
                if (methodHandler.CanProcess(exp))
                {
                    methodHandler.Process(exp, this);
                    return exp;
                }
            }

            bool IsDefinedDbFunctionAttribute = exp.Method.IsDefined(typeof(DbFunctionAttribute));
            if (IsDefinedDbFunctionAttribute)
            {
                return this.VisitDbFunctionMethodCallExpression(exp);
            }

            if (exp.IsEvaluable())
            {
                DbParameterExpression dbParameter = new DbParameterExpression(exp.Evaluate(), exp.Type);
                return dbParameter.Accept(this);
            }

            throw PublicHelper.MakeNotSupportedMethodException(exp.Method);
        }

        public override DbExpression Visit(DbMemberExpression exp)
        {
            MemberInfo member = exp.Member;

            if (member.Name == "Length" && member.DeclaringType == PublicConstants.TypeOfString)
            {
                return this.VisitStringLengthMemberAccessExpression(exp);
            }

            if (member.Name == "Value" && ReflectionExtension.IsNullable(exp.Expression.Type))
            {
                exp.Expression.Accept(this);
                return exp;
            }

            DbParameterExpression newExp;
            if (DbExpressionExtension.TryConvertToParameterExpression(exp, out newExp))
            {
                return newExp.Accept(this);
            }

            throw new NotSupportedException(string.Format("'{0}.{1}' is not supported.", member.DeclaringType.FullName, member.Name));
        }

        protected virtual DbExpression VisitStringLengthMemberAccessExpression(DbMemberExpression exp)
        {
            this.SqlBuilder.Append("LENGTH(");
            exp.Expression.Accept(this);
            this.SqlBuilder.Append(")");

            return exp;
        }
        protected virtual DbExpression VisitDbFunctionMethodCallExpression(DbMethodCallExpression exp)
        {
            DbFunctionAttribute dbFunction = exp.Method.GetCustomAttribute<DbFunctionAttribute>();
            string schema = dbFunction.Schema;
            string functionName = string.IsNullOrEmpty(dbFunction.Name) ? exp.Method.Name : dbFunction.Name;

            if (!string.IsNullOrEmpty(schema))
            {
                this.QuoteName(schema);
                this.SqlBuilder.Append(".");
            }

            this.QuoteName(functionName);
            this.SqlBuilder.Append("(");

            string c = "";
            foreach (DbExpression argument in exp.Arguments)
            {
                this.SqlBuilder.Append(c);
                argument.Accept(this);
                c = ",";
            }

            this.SqlBuilder.Append(")");

            return exp;
        }

        public virtual void QuoteName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name");

            this.SqlBuilder.Append(this.LeftQuoteChar, name, this.RightQuoteChar);
        }
        protected virtual void AppendTable(DbTable table)
        {
            if (!string.IsNullOrEmpty(table.Schema))
            {
                this.QuoteName(table.Schema);
                this.SqlBuilder.Append(".");
            }

            this.QuoteName(table.Name);
        }
        protected virtual void AppendTableSegment(DbTableSegment seg)
        {
            seg.Body.Accept(this);
            this.SqlBuilder.Append(" AS ");
            this.QuoteName(seg.Alias);
        }
        protected virtual void AppendColumnSegment(DbColumnSegment seg)
        {
            seg.Body.Accept(this);
            this.SqlBuilder.Append(" AS ");
            this.QuoteName(seg.Alias);
        }
        protected void ConcatOperands(IEnumerable<DbExpression> operands, string connector)
        {
            this.SqlBuilder.Append("(");

            bool first = true;
            foreach (DbExpression operand in operands)
            {
                if (first)
                    first = false;
                else
                    this.SqlBuilder.Append(connector);

                operand.Accept(this);
            }

            this.SqlBuilder.Append(")");
            return;
        }
        protected void VisitDbJoinTableExpressions(List<DbJoinTableExpression> tables)
        {
            foreach (var table in tables)
            {
                table.Accept(this);
            }
        }

        protected virtual void BuildWhereState(DbExpression whereExpression)
        {
            if (whereExpression != null)
            {
                this.SqlBuilder.Append(" WHERE ");
                whereExpression.Accept(this);
            }
        }
        protected virtual void BuildOrderState(List<DbOrdering> orderings)
        {
            if (orderings.Count > 0)
            {
                this.SqlBuilder.Append(" ORDER BY ");
                this.ConcatOrderings(orderings);
            }
        }
        protected virtual void ConcatOrderings(List<DbOrdering> orderings)
        {
            for (int i = 0; i < orderings.Count; i++)
            {
                if (i > 0)
                {
                    this.SqlBuilder.Append(",");
                }

                this.AppendOrdering(orderings[i]);
            }
        }
        protected virtual void BuildGroupState(DbSqlQueryExpression exp)
        {
            var groupSegments = exp.GroupSegments;
            if (groupSegments.Count == 0)
                return;

            this.SqlBuilder.Append(" GROUP BY ");
            for (int i = 0; i < groupSegments.Count; i++)
            {
                if (i > 0)
                    this.SqlBuilder.Append(",");

                groupSegments[i].Accept(this);
            }

            if (exp.HavingCondition != null)
            {
                this.SqlBuilder.Append(" HAVING ");
                exp.HavingCondition.Accept(this);
            }
        }

        protected virtual void BuildCastState(DbExpression castExp, string targetDbTypeString)
        {
            this.SqlBuilder.Append("CAST(");
            castExp.Accept(this);
            this.SqlBuilder.Append(" AS ", targetDbTypeString, ")");
        }
        protected void BuildCastState(object castObject, string targetDbTypeString)
        {
            this.SqlBuilder.Append("CAST(", castObject, " AS ", targetDbTypeString, ")");
        }

        protected void AppendOrdering(DbOrdering ordering)
        {
            if (ordering.OrderType == DbOrderType.Asc)
            {
                ordering.Expression.Accept(this);
                this.SqlBuilder.Append(" ASC");
                return;
            }
            else if (ordering.OrderType == DbOrderType.Desc)
            {
                ordering.Expression.Accept(this);
                this.SqlBuilder.Append(" DESC");
                return;
            }

            throw new NotSupportedException("OrderType: " + ordering.OrderType);
        }
    }
}
