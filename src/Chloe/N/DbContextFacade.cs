﻿using Chloe.Infrastructure.Interception;
using Chloe.Routing;
using Chloe.Sharding;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Text;

namespace Chloe
{
    public class DbContextFacade : DbContextBase, IDbContextFacade
    {
        public const string DefaultDataSourceName = "_default_";

        bool _disposed = false;
        IDbSessionFacade _session;


        public DbContextFacade(IDbContextProviderFactory dbContextProviderFactory)
        {
            this.DbContextProviderFactory = dbContextProviderFactory;
            this.Butler = new DbContextButler(this);
            this._session = new DbSessionImpl(this);
        }

        internal IDbContextProviderFactory DbContextProviderFactory { get; set; }
        internal DbContextButler Butler { get; private set; }
        public override IDbSessionFacade Session { get { return this._session; } }
        public IDbContextProvider DefaultDbContextProvider { get { return this.Butler.GetDefaultDbContextProvider(); } }
        internal IDbContextProvider ShardingDbContextProvider { get { return this.Butler.GetShardingDbContextProvider(); } }

        bool IsShardingType(Type entityType)
        {
            IShardingConfig shardingConfig = ShardingConfigContainer.Find(entityType);
            return shardingConfig != null;
        }
        IDbContextProvider GetDbContextProvider(Type entityType)
        {
            bool isShardingType = this.IsShardingType(entityType);
            if (!isShardingType)
            {
                return this.DefaultDbContextProvider;
            }

            return this.ShardingDbContextProvider;
        }
        IDbContextProvider GetDbContextProvider<TEntity>()
        {
            return this.GetDbContextProvider(typeof(TEntity));
        }


        protected override void Dispose(bool disposing)
        {
            this.Butler.Dispose();
        }

        public override void TrackEntity(object entity)
        {
            PublicHelper.CheckNull(entity);
            var entityType = entity.GetType();
            this.GetDbContextProvider(entityType).TrackEntity(entity);
        }

        public override IQuery<TEntity> Query<TEntity>(string table, LockType @lock)
        {
            return this.GetDbContextProvider<TEntity>().Query<TEntity>(table, @lock);
        }

        public override List<T> SqlQuery<T>(string sql, CommandType cmdType, params DbParam[] parameters)
        {
            return this.DefaultDbContextProvider.SqlQuery<T>(sql, cmdType, parameters);
        }
        public override List<T> SqlQuery<T>(string sql, CommandType cmdType, object parameter)
        {
            /*
             * Usage:
             * dbContext.SqlQuery<User>("select * from Users where Id=@Id", CommandType.Text, new { Id = 1 });
             */

            return this.DefaultDbContextProvider.SqlQuery<T>(sql, cmdType, parameter);
        }
        public override Task<List<T>> SqlQueryAsync<T>(string sql, CommandType cmdType, params DbParam[] parameters)
        {
            return this.DefaultDbContextProvider.SqlQueryAsync<T>(sql, cmdType, parameters);
        }
        public override Task<List<T>> SqlQueryAsync<T>(string sql, CommandType cmdType, object parameter)
        {
            return this.DefaultDbContextProvider.SqlQueryAsync<T>(sql, cmdType, parameter);
        }

        public override TEntity Save<TEntity>(TEntity entity)
        {
            return this.GetDbContextProvider<TEntity>().Save(entity);
        }
        public override Task<TEntity> SaveAsync<TEntity>(TEntity entity)
        {
            return this.GetDbContextProvider<TEntity>().SaveAsync(entity);
        }

        protected override Task<TEntity> Insert<TEntity>(TEntity entity, string table, bool @async)
        {
            var dbContextProvider = this.GetDbContextProvider<TEntity>();
            if (@async)
            {
                return dbContextProvider.InsertAsync(entity, table);
            }

            return Task.FromResult(dbContextProvider.Insert(entity, table));
        }
        protected override Task<object> Insert<TEntity>(Expression<Func<TEntity>> content, string table, bool @async)
        {
            var dbContextProvider = this.GetDbContextProvider<TEntity>();
            if (@async)
            {
                return dbContextProvider.InsertAsync(content, table);
            }

            return Task.FromResult(dbContextProvider.Insert(content, table));
        }
        protected override Task InsertRange<TEntity>(List<TEntity> entities, string table, bool @async)
        {
            var dbContextProvider = this.GetDbContextProvider<TEntity>();
            if (@async)
            {
                return dbContextProvider.InsertRangeAsync(entities, table);
            }

            dbContextProvider.InsertRange(entities, table);
            return Task.CompletedTask;
        }

        protected override Task<int> Update<TEntity>(TEntity entity, string table, bool @async)
        {
            var dbContextProvider = this.GetDbContextProvider<TEntity>();
            if (@async)
            {
                return dbContextProvider.UpdateAsync(entity, table);
            }

            return Task.FromResult(dbContextProvider.Update(entity, table));
        }
        protected override Task<int> Update<TEntity>(Expression<Func<TEntity, bool>> condition, Expression<Func<TEntity, TEntity>> content, string table, bool @async)
        {
            var dbContextProvider = this.GetDbContextProvider<TEntity>();
            if (@async)
            {
                return dbContextProvider.UpdateAsync(condition, content, table);
            }

            return Task.FromResult(dbContextProvider.Update(condition, content, table));
        }

        protected override Task<int> Delete<TEntity>(TEntity entity, string table, bool @async)
        {
            var dbContextProvider = this.GetDbContextProvider<TEntity>();
            if (@async)
            {
                return dbContextProvider.DeleteAsync(entity, table);
            }

            return Task.FromResult(dbContextProvider.Delete(entity, table));
        }
        protected override Task<int> Delete<TEntity>(Expression<Func<TEntity, bool>> condition, string table, bool @async)
        {
            var dbContextProvider = this.GetDbContextProvider<TEntity>();
            if (@async)
            {
                return dbContextProvider.DeleteAsync(condition, table);
            }

            return Task.FromResult(dbContextProvider.Delete(condition, table));
        }

        public override ITransientTransaction BeginTransaction()
        {
            /*
             * using(ITransientTransaction tran = dbContext.BeginTransaction())
             * {
             *      dbContext.Insert()...
             *      dbContext.Update()...
             *      dbContext.Delete()...
             *      tran.Commit();
             * }
             */
            return new TransientTransaction(this);
        }
        public override ITransientTransaction BeginTransaction(IsolationLevel il)
        {
            return new TransientTransaction(this, il);
        }
        public override void UseTransaction(Action action)
        {
            /*
             * dbContext.UseTransaction(() =>
             * {
             *     dbContext.Insert()...
             *     dbContext.Update()...
             *     dbContext.Delete()...
             * });
             */

            PublicHelper.CheckNull(action);
            using (ITransientTransaction tran = this.BeginTransaction())
            {
                action();
                tran.Commit();
            }
        }
        public override void UseTransaction(Action action, IsolationLevel il)
        {
            PublicHelper.CheckNull(action);
            using (ITransientTransaction tran = this.BeginTransaction(il))
            {
                action();
                tran.Commit();
            }
        }
        public override async Task UseTransaction(Func<Task> func)
        {
            /*
             * await dbContext.UseTransaction(async () =>
             * {
             *     await dbContext.InsertAsync()...
             *     await dbContext.UpdateAsync()...
             *     await dbContext.DeleteAsync()...
             * });
             */

            PublicHelper.CheckNull(func);
            using (ITransientTransaction tran = this.BeginTransaction())
            {
                await func();
                tran.Commit();
            }
        }
        public override async Task UseTransaction(Func<Task> func, IsolationLevel il)
        {
            PublicHelper.CheckNull(func);
            using (ITransientTransaction tran = this.BeginTransaction(il))
            {
                await func();
                tran.Commit();
            }
        }
    }
}
