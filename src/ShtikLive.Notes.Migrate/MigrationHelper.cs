﻿using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using ShtikLive.Notes.Data;

namespace ShtikLive.Notes.Migrate
{
    public class MigrationHelper
    {
        private readonly ILogger<MigrationHelper> _logger;

        public MigrationHelper(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<MigrationHelper>();
        }

        public async Task TryMigrate(DbContextOptions<NoteContext> options)
        {
            await TryMigrate(new NoteContext(options));
        }

        public async Task TryMigrate(NoteContext context)
        {
            using (context)
            {
                await TryConnect(context);

                await TryRunMigration(context);
            }
        }

        private async Task TryRunMigration(NoteContext context)
        {
            try
            {
                await context.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                // Ignored
                _logger.LogError(EventIds.MigrationFailed, ex, "Migration failed to run: {message}", ex.Message);
            }
        }

        private async Task TryConnect(NoteContext context)
        {
            var connectionString = context.Database.GetDbConnection().ConnectionString;
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = null };
            connectionString = builder.ConnectionString;

            try
            {
                await Policy
                    .Handle<Exception>((ex) =>
                    {
                        _logger.LogWarning(EventIds.MigrationTestConnectFailed, ex, "TryMigrate test connect failed, retrying.");
                        return true;
                    })
                    .WaitAndRetryAsync(5, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)))
                    .ExecuteAsync(async () =>
                    {
                        using (var connection = new NpgsqlConnection(connectionString))
                        {
                            await connection.OpenAsync();
                            Console.WriteLine($"Connected: {connectionString}");
                        }
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(EventIds.MigrationTestConnectFailed, ex, "TryMigrate could not connect to database.");
                throw;
            }
        }
    }
}