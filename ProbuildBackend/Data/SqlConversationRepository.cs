// ProbuildBackend/Data/SqlConversationRepository.cs
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;

public class SqlConversationRepository : IConversationRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqlConversationRepository> _logger;

    public SqlConversationRepository(
        IConfiguration configuration,
        ILogger<SqlConversationRepository> logger
    )
    {
#if (DEBUG)
        _connectionString = configuration.GetConnectionString("DefaultConnection");
#else
        _connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
#endif
        _logger = logger;
    }

    private SqlConnection GetConnection() => new SqlConnection(_connectionString);

    public async Task<string> CreateConversationAsync(
        string userId,
        string title,
        List<string>? promptKeys = null
    )
    {
        _logger.LogInformation(
            "START: CreateConversationAsync for User {UserId}, Title: {Title}",
            userId,
            title
        );
        await using var connection = GetConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            var newConversation = new Conversation
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Title = title,
                CreatedAt = DateTime.UtcNow,
            };

            var conversationSql =
                @"INSERT INTO Conversations (Id, UserId, Title, CreatedAt) VALUES (@Id, @UserId, @Title, @CreatedAt);";
            _logger.LogInformation(
                "Executing SQL to insert new conversation: {ConversationId}",
                newConversation.Id
            );
            await connection.ExecuteAsync(conversationSql, newConversation, transaction);

            if (promptKeys != null && promptKeys.Any())
            {
                _logger.LogInformation(
                    "Inserting {PromptCount} prompt keys for conversation {ConversationId}",
                    promptKeys.Count,
                    newConversation.Id
                );
                var promptKeySql =
                    @"INSERT INTO ConversationPrompts (ConversationId, PromptKey) VALUES (@ConversationId, @PromptKey);";
                foreach (var key in promptKeys)
                {
                    await connection.ExecuteAsync(
                        promptKeySql,
                        new { ConversationId = newConversation.Id, PromptKey = key },
                        transaction
                    );
                }
            }

            transaction.Commit();
            _logger.LogInformation(
                "Successfully created conversation {ConversationId}",
                newConversation.Id
            );
            return newConversation.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXCEPTION in CreateConversationAsync for User {UserId}", userId);
            transaction.Rollback();
            throw;
        }
        finally
        {
            _logger.LogInformation("END: CreateConversationAsync for User {UserId}", userId);
        }
    }

    public async Task<Conversation?> GetConversationAsync(string conversationId)
    {
        await using var connection = GetConnection();
        var sql =
            "SELECT * FROM Conversations WHERE Id = @Id;"
            + "SELECT * FROM ConversationPrompts WHERE ConversationId = @Id;";

        using (var multi = await connection.QueryMultipleAsync(sql, new { Id = conversationId }))
        {
            var conversation = await multi.ReadSingleOrDefaultAsync<Conversation>();
            if (conversation != null)
            {
                conversation.PromptKeys = (await multi.ReadAsync<ConversationPrompt>()).ToList();
            }
            return conversation;
        }
    }

    public async Task<List<Message>> GetUnsummarizedMessagesAsync(string conversationId)
    {
        await using var connection = GetConnection();
        var sql =
            "SELECT * FROM Messages WHERE ConversationId = @ConversationId AND IsSummarized = 0 ORDER BY Timestamp ASC;";
        var messages = await connection.QueryAsync<Message>(
            sql,
            new { ConversationId = conversationId }
        );
        return messages.ToList();
    }

    public async Task AddMessageAsync(Message message)
    {
        try
        {
            await using var connection = GetConnection();
            message.Timestamp = DateTime.UtcNow;
            var sql =
                @"INSERT INTO Messages (ConversationId, Role, Content, IsSummarized, Timestamp) VALUES (@ConversationId, @Role, @Content, @IsSummarized, @Timestamp);";
            await connection.ExecuteAsync(sql, message);
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex,
                "Error inserting message for Conversation {ConversationId}",
                message.ConversationId
            );
            throw;
        }
    }

    public async Task UpdateConversationSummaryAsync(string conversationId, string? newSummary)
    {
        await using var connection = GetConnection();
        var sql = "UPDATE Conversations SET ConversationSummary = @Summary WHERE Id = @Id;";
        await connection.ExecuteAsync(sql, new { Summary = newSummary, Id = conversationId });
    }

    public async Task MarkMessagesAsSummarizedAsync(IEnumerable<long> messageIds)
    {
        if (messageIds == null || !messageIds.Any())
            return;
        await using var connection = GetConnection();
        var sql = "UPDATE Messages SET IsSummarized = 1 WHERE Id IN @Ids;";
        await connection.ExecuteAsync(sql, new { Ids = messageIds.ToList() });
    }

    public async Task<List<Message>> GetMessagesAsync(
        string conversationId,
        bool includeSummarized = true
    )
    {
        await using var connection = GetConnection();
        var sqlBuilder = new StringBuilder(
            "SELECT * FROM Messages WHERE ConversationId = @ConversationId "
        );
        if (!includeSummarized)
        {
            sqlBuilder.Append("AND IsSummarized = 0 ");
        }
        sqlBuilder.Append("ORDER BY Timestamp ASC;");
        var messages = await connection.QueryAsync<Message>(
            sqlBuilder.ToString(),
            new { ConversationId = conversationId }
        );
        return messages.ToList();
    }

    public async Task<IEnumerable<Conversation>> GetByUserIdAsync(string userId)
    {
        await using var connection = GetConnection();
        var sql =
            "SELECT * FROM Conversations WHERE UserId = @UserId;"
            + "SELECT cp.* FROM ConversationPrompts cp JOIN Conversations c ON cp.ConversationId = c.Id WHERE c.UserId = @UserId;";

        using (var multi = await connection.QueryMultipleAsync(sql, new { UserId = userId }))
        {
            var conversations = (await multi.ReadAsync<Conversation>()).ToList();
            var promptKeys = (await multi.ReadAsync<ConversationPrompt>()).ToList();

            foreach (var conversation in conversations)
            {
                conversation.PromptKeys = promptKeys
                    .Where(pk => pk.ConversationId == conversation.Id)
                    .ToList();
            }
            return conversations;
        }
    }

    public async Task UpdateConversationTitleAsync(string conversationId, string newTitle)
    {
        await using var connection = GetConnection();
        var sql = "UPDATE Conversations SET Title = @NewTitle WHERE Id = @ConversationId";
        await connection.ExecuteAsync(
            sql,
            new { NewTitle = newTitle, ConversationId = conversationId }
        );
    }
}
