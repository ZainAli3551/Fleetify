using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Fleetify.Services.Interfaces;

namespace Fleetify.Controllers.Api
{
    [ApiController]
    [Route("api/support")]
    public class SupportApiController : ControllerBase
    {
        private readonly ISupportService _supportService;

        public SupportApiController(ISupportService supportService)
        {
            _supportService = supportService;
        }

        public class InitChatRequest
        {
            public string? SessionToken { get; set; }
            public string? Name { get; set; }
            public string? Email { get; set; }
        }

        public class SendMessageRequest
        {
            public int ConversationId { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        public class EscalateRequest
        {
            public int ConversationId { get; set; }
            public string? Reason { get; set; }
        }

        // POST: /api/support/init
        [HttpPost("init")]
        public async Task<IActionResult> InitChat([FromBody] InitChatRequest req)
        {
            string token = string.IsNullOrWhiteSpace(req?.SessionToken) ? Guid.NewGuid().ToString("N") : req.SessionToken;
            
            int? userId = null;
            string? name = req?.Name;
            string? email = req?.Email;

            if (User.Identity?.IsAuthenticated == true)
            {
                var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(idStr, out int id))
                {
                    userId = id;
                }
                name = User.Identity.Name ?? name;
                email = User.FindFirstValue(ClaimTypes.Email) ?? email;
            }

            var conv = await _supportService.GetOrCreateConversationAsync(token, email, name, userId);

            return Ok(new
            {
                success = true,
                conversationId = conv.ConversationID,
                sessionToken = conv.SessionToken,
                userName = conv.UserName,
                status = conv.Status,
                messages = conv.Messages.OrderBy(m => m.SentAt).Select(m => new
                {
                    messageID = m.MessageID,
                    conversationID = m.ConversationID,
                    senderType = m.SenderType,
                    senderName = m.SenderName,
                    messageText = m.MessageText,
                    sentAt = m.SentAt
                })
            });
        }

        // POST: /api/support/message
        [HttpPost("message")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest req)
        {
            if (req == null || req.ConversationId <= 0 || string.IsNullOrWhiteSpace(req.Message))
            {
                return BadRequest(new { success = false, message = "Invalid message payload." });
            }

            try
            {
                var botReply = await _supportService.ProcessUserMessageAsync(req.ConversationId, req.Message);
                var conv = await _supportService.GetConversationDetailsAsync(req.ConversationId);

                var userMsg = conv?.Messages
                    .Where(m => m.SenderType == "User")
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefault();

                return Ok(new
                {
                    success = true,
                    userMessage = userMsg != null ? new
                    {
                        messageID = userMsg.MessageID,
                        conversationID = userMsg.ConversationID,
                        senderType = userMsg.SenderType,
                        senderName = userMsg.SenderName,
                        messageText = userMsg.MessageText,
                        sentAt = userMsg.SentAt
                    } : null,
                    botReply = new
                    {
                        messageID = botReply.MessageID,
                        conversationID = botReply.ConversationID,
                        senderType = botReply.SenderType,
                        senderName = botReply.SenderName,
                        messageText = botReply.MessageText,
                        sentAt = botReply.SentAt
                    },
                    status = conv?.Status ?? "BotHandled"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // POST: /api/support/escalate
        [HttpPost("escalate")]
        public async Task<IActionResult> Escalate([FromBody] EscalateRequest req)
        {
            if (req == null || req.ConversationId <= 0)
            {
                return BadRequest(new { success = false, message = "Invalid conversation ID." });
            }

            bool ok = await _supportService.EscalateToAdminAsync(req.ConversationId, req.Reason);
            var conv = await _supportService.GetConversationDetailsAsync(req.ConversationId);
            var lastMsg = conv?.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();

            return Ok(new
            {
                success = ok,
                status = conv?.Status ?? "NeedsAdmin",
                message = ok ? "Successfully escalated to Admin." : "Failed to escalate.",
                botReply = lastMsg != null ? new
                {
                    messageID = lastMsg.MessageID,
                    conversationID = lastMsg.ConversationID,
                    senderType = lastMsg.SenderType,
                    senderName = lastMsg.SenderName,
                    messageText = lastMsg.MessageText,
                    sentAt = lastMsg.SentAt
                } : null
            });
        }

        // GET: /api/support/poll/{id}
        [HttpGet("poll/{id}")]
        public async Task<IActionResult> PollMessages(int id)
        {
            var conv = await _supportService.GetConversationDetailsAsync(id);
            if (conv == null)
            {
                return NotFound(new { success = false, message = "Conversation not found." });
            }

            return Ok(new
            {
                success = true,
                conversationId = conv.ConversationID,
                status = conv.Status,
                messages = conv.Messages.OrderBy(m => m.SentAt).Select(m => new
                {
                    messageID = m.MessageID,
                    conversationID = m.ConversationID,
                    senderType = m.SenderType,
                    senderName = m.SenderName,
                    messageText = m.MessageText,
                    sentAt = m.SentAt
                })
            });
        }
    }
}
