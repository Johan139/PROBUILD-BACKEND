// ProbuildBackend/Data/SqlConversationRepository.cs
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text;

public class SqlConversationRepository : IConversationRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqlConversationRepository> _logger;
    public SqlConversationRepository(IConfiguration configuration, ILogger<SqlConversationRepository> logger)
    {
#if (DEBUG)
        _connectionString = configuration.GetConnectionString("DefaultConnection");
#else
 _connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
#endif
        _logger = logger;
    }

    private SqlConnection GetConnection() => new SqlConnection(_connectionString);

    public async Task<string> CreateConversationAsync(string userId, string title)
    {
       await using var connection = GetConnection();
        var newConversation = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Title = title,
            CreatedAt = DateTime.UtcNow
        };
        var sql = @"INSERT INTO Conversations (Id, UserId, Title, CreatedAt) VALUES (@Id, @UserId, @Title, @CreatedAt);";
        await connection.ExecuteAsync(sql, newConversation);
        return newConversation.Id;
    }

    public async Task<Conversation?> GetConversationAsync(string conversationId)
    {
        await using var connection = GetConnection();
        var sql = "SELECT * FROM Conversations WHERE Id = @Id";
        return await connection.QuerySingleOrDefaultAsync<Conversation>(sql, new { Id = conversationId });
    }
    
    public async Task<List<Message>> GetUnsummarizedMessagesAsync(string conversationId)
    {
        await using var connection = GetConnection();
        var sql = "SELECT * FROM Messages WHERE ConversationId = @ConversationId AND IsSummarized = 0 ORDER BY Timestamp ASC;";
        var messages = await connection.QueryAsync<Message>(sql, new { ConversationId = conversationId });
        return messages.ToList();
    }

    public async Task AddMessageAsync(Message message)
    {
        try
        {


            await using var connection = GetConnection();
        message.Timestamp = DateTime.UtcNow;
        var sql = @"INSERT INTO Messages (ConversationId, Role, Content, IsSummarized, Timestamp) VALUES (@ConversationId, @Role, @Content, @IsSummarized, @Timestamp);";
        await connection.ExecuteAsync(sql, message);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error inserting message for Conversation {ConversationId}", message.ConversationId);
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
        if (messageIds == null || !messageIds.Any()) return;
        await using var connection = GetConnection();
        var sql = "UPDATE Messages SET IsSummarized = 1 WHERE Id IN @Ids;";
        await connection.ExecuteAsync(sql, new { Ids = messageIds.ToList() });
    }

    public async Task<List<Message>> GetMessagesAsync(string conversationId, bool includeSummarized = true)
    {
        await using var connection = GetConnection();
        var sqlBuilder = new StringBuilder("SELECT * FROM Messages WHERE ConversationId = @ConversationId ");
        if (!includeSummarized)
        {
            sqlBuilder.Append("AND IsSummarized = 0 ");
        }
        sqlBuilder.Append("ORDER BY Timestamp ASC;");
        var messages = await connection.QueryAsync<Message>(sqlBuilder.ToString(), new { ConversationId = conversationId });
        return messages.ToList();
    }
}