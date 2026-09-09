using System.Collections.Generic;
using System.Threading.Tasks;
using Fleetify.Models.Entities;

namespace Fleetify.Services.Interfaces
{
    public interface IGeminiService
    {
        bool IsConfigured { get; }
        Task<string?> GenerateReplyAsync(string userMessage, List<SupportMessage>? conversationHistory = null, string? liveContext = null);
    }
}
