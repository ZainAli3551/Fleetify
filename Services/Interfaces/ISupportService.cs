using System.Collections.Generic;
using System.Threading.Tasks;
using Fleetify.Models.Entities;

namespace Fleetify.Services.Interfaces
{
    public interface ISupportService
    {
        Task<SupportConversation> GetOrCreateConversationAsync(string sessionToken, string? email = null, string? name = null, int? userId = null);
        Task<SupportMessage> ProcessUserMessageAsync(int conversationId, string userMessage);
        Task<SupportMessage> AddAdminReplyAsync(int conversationId, int adminId, string adminName, string replyMessage, bool resolve = false);
        Task<List<SupportConversation>> GetConversationsAsync(string? statusFilter = null);
        Task<SupportConversation?> GetConversationDetailsAsync(int conversationId);
        Task<bool> EscalateToAdminAsync(int conversationId, string? reason = null);
        Task<bool> ResolveTicketAsync(int conversationId);
        Task<int> GetNeedsAdminCountAsync();
    }
}
