using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Fleetify.Data;
using Fleetify.Models.Entities;
using Fleetify.Services.Interfaces;

namespace Fleetify.Services.Implementations
{
    public class SupportService : ISupportService
    {
        private readonly FleetifyDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IGeminiService _geminiService;

        public SupportService(FleetifyDbContext context, INotificationService notificationService, IGeminiService geminiService)
        {
            _context = context;
            _notificationService = notificationService;
            _geminiService = geminiService;
        }

        public async Task<SupportConversation> GetOrCreateConversationAsync(string sessionToken, string? email = null, string? name = null, int? userId = null)
        {
            var conversation = await _context.SupportConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.SessionToken == sessionToken);

            if (conversation == null)
            {
                conversation = new SupportConversation
                {
                    SessionToken = sessionToken,
                    UserID = userId,
                    UserName = !string.IsNullOrWhiteSpace(name) ? name : "Guest Customer",
                    UserEmail = !string.IsNullOrWhiteSpace(email) ? email : "support@fleetify.local",
                    Subject = "General Support Inquiry",
                    Status = "BotHandled",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Add welcome greeting from AI Bot
                conversation.Messages.Add(new SupportMessage
                {
                    SenderType = "Bot",
                    SenderName = "Fleetify AI Assistant",
                    MessageText = "👋 Hello! I am Fleetify's AI Virtual Assistant. How can I help you today? You can ask me about tracking packages, cost estimates, vehicle capacities, or request human admin support anytime.",
                    SentAt = DateTime.UtcNow
                });

                _context.SupportConversations.Add(conversation);
                await _context.SaveChangesAsync();
            }
            else
            {
                // Update user details if provided later
                if (userId.HasValue && !conversation.UserID.HasValue)
                {
                    conversation.UserID = userId;
                }
                if (!string.IsNullOrWhiteSpace(name) && conversation.UserName == "Guest Customer")
                {
                    conversation.UserName = name;
                }
                if (!string.IsNullOrWhiteSpace(email) && conversation.UserEmail == "support@fleetify.local")
                {
                    conversation.UserEmail = email;
                }
                await _context.SaveChangesAsync();
            }

            return conversation;
        }

        public async Task<SupportMessage> ProcessUserMessageAsync(int conversationId, string userMessage)
        {
            var conversation = await _context.SupportConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.ConversationID == conversationId);

            if (conversation == null)
            {
                throw new ArgumentException("Conversation not found.");
            }

            // 1. Record user's incoming message
            var userMsg = new SupportMessage
            {
                ConversationID = conversationId,
                SenderType = "User",
                SenderName = conversation.UserName,
                MessageText = userMessage.Trim(),
                SentAt = DateTime.UtcNow
            };
            _context.SupportMessages.Add(userMsg);
            conversation.UpdatedAt = DateTime.UtcNow;

            // 2. Prepare Live Database Context for AI (e.g. order tracking, active customer shipments)
            string? liveContext = null;
            var trackingMatch = Regex.Match(userMessage.ToUpper(), @"FLT-\d{4}-\d+");
            if (trackingMatch.Success)
            {
                string trackingNumber = trackingMatch.Value;
                var order = await _context.DeliveryRequests
                    .Include(d => d.Assignment)
                        .ThenInclude(a => a!.Driver)
                    .FirstOrDefaultAsync(d => d.TrackingNumber == trackingNumber);

                if (order != null)
                {
                    string driverInfo = order.Assignment?.Driver != null
                        ? $"{order.Assignment.Driver.FullName} (Phone: {order.Assignment.Driver.PhoneNumber})"
                        : "Awaiting Driver Dispatch";

                    liveContext = $"Database Record for Tracking Code '{trackingNumber}':\n" +
                                  $"• Current Status: {order.RequestedStatus}\n" +
                                  $"• Route: {order.PickupLocation} -> {order.DropoffLocation}\n" +
                                  $"• Assigned Driver: {driverInfo}\n" +
                                  $"• Estimated Fare: ${order.EstimatedCost:F2}\n" +
                                  $"• Vehicle Type: {order.VehicleType}\n" +
                                  $"• Created At: {order.RequestDate:yyyy-MM-dd HH:mm} UTC";
                }
                else
                {
                    liveContext = $"Database search for tracking code '{trackingNumber}' returned NO MATCHING ORDER.";
                }
            }
            else if (conversation.UserID.HasValue)
            {
                var recentOrder = await _context.DeliveryRequests
                    .Where(d => d.UserID == conversation.UserID.Value)
                    .OrderByDescending(d => d.RequestDate)
                    .FirstOrDefaultAsync();

                if (recentOrder != null)
                {
                    liveContext = $"User's Most Recent Shipment Record: Tracking {recentOrder.TrackingNumber}, Status: {recentOrder.RequestedStatus}, Route: {recentOrder.PickupLocation} -> {recentOrder.DropoffLocation}, Cost: ${recentOrder.EstimatedCost:F2}";
                }
            }

            // 3. Attempt to generate response using Google Gemini Generative AI
            string? geminiReply = null;
            if (_geminiService.IsConfigured)
            {
                geminiReply = await _geminiService.GenerateReplyAsync(userMessage, conversation.Messages.ToList(), liveContext);
            }

            string botReplyText;

            if (!string.IsNullOrWhiteSpace(geminiReply))
            {
                // Check if Gemini detected a need for human administrator escalation
                if (geminiReply.Contains("[ESCALATE_TO_ADMIN]", StringComparison.OrdinalIgnoreCase))
                {
                    geminiReply = geminiReply.Replace("[ESCALATE_TO_ADMIN]", "").Trim();
                    await EscalateToAdminAsync(conversationId, "Inquiry flagged for human admin intervention");
                }

                botReplyText = geminiReply;
            }
            else
            {
                // Deterministic Rule-Based Fallback
                string cleanText = userMessage.ToLower().Trim();

                // A. Check for Order Tracking
                if (trackingMatch.Success || cleanText.Contains("flt-") || cleanText.Contains("track"))
                {
                    string trackingNumber = trackingMatch.Success ? trackingMatch.Value : string.Empty;
                    if (!string.IsNullOrEmpty(trackingNumber))
                    {
                        var order = await _context.DeliveryRequests
                            .Include(d => d.Assignment)
                                .ThenInclude(a => a!.Driver)
                            .FirstOrDefaultAsync(d => d.TrackingNumber == trackingNumber);

                        if (order != null)
                        {
                            string driverInfo = order.Assignment?.Driver != null
                                ? $"{order.Assignment.Driver.FullName} ({order.Assignment.Driver.PhoneNumber})"
                                : "Awaiting Driver Dispatch";

                            botReplyText = $"📦 **Shipment Found: {order.TrackingNumber}**\n" +
                                           $"• **Current Status:** {order.RequestedStatus}\n" +
                                           $"• **Route:** {order.PickupLocation} ➔ {order.DropoffLocation}\n" +
                                           $"• **Assigned Driver:** {driverInfo}\n" +
                                           $"• **Estimated Fare:** ${order.EstimatedCost:F2}\n" +
                                           $"• **Vehicle Required:** {order.VehicleType}";
                        }
                        else
                        {
                            botReplyText = $"🔍 I searched our database for tracking code **{trackingNumber}**, but could not locate an active order. Please verify the code or ask for Administrator assistance.";
                        }
                    }
                    else
                    {
                        botReplyText = "📦 To track a delivery, please provide your **Tracking Number** (e.g., `FLT-2026-302107`). I will pull up the live status immediately!";
                    }
                }
                // B. Check for Escalation / Human Agent
                else if (cleanText.Contains("admin") || cleanText.Contains("human") || cleanText.Contains("agent") ||
                         cleanText.Contains("person") || cleanText.Contains("representative") || cleanText.Contains("operator") ||
                         cleanText.Contains("shikayat") || cleanText.Contains("complaint") || cleanText.Contains("damaged") ||
                         cleanText.Contains("broken") || cleanText.Contains("refund") || cleanText.Contains("lost") ||
                         cleanText.Contains("urgent") || cleanText.Contains("fraud") || cleanText.Contains("kharab"))
                {
                    await EscalateToAdminAsync(conversationId, userMessage);
                    botReplyText = "🚨 **Connecting with Fleetify Administrator...**\n\n" +
                                   "I have flagged this ticket for **Human Administrator Intervention**. An admin has been notified and will review this chat thread and intervene directly here shortly. You can continue sending details below.";
                }
                // C. Cost Estimation & Rates
                else if (cleanText.Contains("cost") || cleanText.Contains("price") || cleanText.Contains("rate") ||
                         cleanText.Contains("fare") || cleanText.Contains("calculate") || cleanText.Contains("kitna") ||
                         cleanText.Contains("pricing") || cleanText.Contains("kharcha"))
                {
                    // Check if message provides distance and weight to calculate dynamically
                    var kmMatch = Regex.Match(cleanText, @"(\d+(\.\d+)?)\s*(km|kilometer)");
                    var kgMatch = Regex.Match(cleanText, @"(\d+(\.\d+)?)\s*(kg|kilo|gram)");

                    if (kmMatch.Success && kgMatch.Success &&
                        double.TryParse(kmMatch.Groups[1].Value, out double distVal) &&
                        double.TryParse(kgMatch.Groups[1].Value, out double wtVal))
                    {
                        double distCharge = Math.Round(distVal * 1.50, 2);
                        double wtCharge = Math.Round(wtVal * 1.50, 2);
                        double totalEst = 10.00 + distCharge + wtCharge;

                        botReplyText = $"💰 **Calculated Delivery Cost Estimate:**\n\n" +
                                       $"• **Base Booking Fare:** $10.00\n" +
                                       $"• **Distance Charge ({distVal:F1} km × $1.50):** ${distCharge:F2}\n" +
                                       $"• **Weight Charge ({wtVal:F1} kg × $1.50):** ${wtCharge:F2}\n" +
                                       $"-------------------------------------\n" +
                                       $"• **Total Estimated Cost:** **${totalEst:F2}**\n\n" +
                                       $"*Note: Vehicle type (Van 1.25x / Truck 1.6x) and Priority (Express 1.35x) may apply on checkout.*";
                    }
                    else
                    {
                        botReplyText = "💰 **Fleetify Delivery Cost Calculation Formula:**\n\n" +
                                       "• **Formula:** Total Cost = Base Fare ($10.00) + (Distance × $1.50) + (Weight × $1.50)\n" +
                                       "• **Base Booking Fare:** $10.00\n" +
                                       "• **Distance Rate:** $1.50 per kilometer\n" +
                                       "• **Weight Rate:** $1.50 per kilogram\n" +
                                       "• **Vehicle Multipliers:** Bike (0.8x), Car (1.0x), Delivery Van (1.25x), Heavy Truck (1.6x)\n" +
                                       "• **Priority Multipliers:** Standard (1.0x), Express (1.35x), Fragile (1.25x), Heavy Cargo (1.5x)\n\n" +
                                       "Agar aap mujhe apna **Distance (km)** aur **Weight (kg)** bata dein, toh main foran exact cost calculate kar ke bata doonga!";
                    }
                }
                // D. Fleet Vehicles & Capacities
                else if (cleanText.Contains("vehicle") || cleanText.Contains("bike") || cleanText.Contains("van") ||
                         cleanText.Contains("truck") || cleanText.Contains("capacity") || cleanText.Contains("wazan") ||
                         cleanText.Contains("gaari"))
                {
                    botReplyText = "🚚 **Fleetify Vehicle Types & Capacity Limits:**\n" +
                                   "• 🏍️ **Motorbike:** Up to 35 kg (Best for letters, legal documents, small parcels)\n" +
                                   "• 🚐 **Delivery Van:** Up to 1,500 kg (Cartons, appliances, mid-sized shipments)\n" +
                                   "• 🚛 **Heavy Truck:** Up to 4,500 kg (Industrial goods, pallets & heavy bulk freight)";
                }
                // E. How to Book
                else if (cleanText.Contains("book") || cleanText.Contains("kaise") || cleanText.Contains("order") ||
                         cleanText.Contains("send") || cleanText.Contains("deliver"))
                {
                    botReplyText = "📋 **Steps to Book a Delivery:**\n" +
                                   "1. Sign in to your Customer Account.\n" +
                                   "2. Go to **Customer Dashboard ➔ New Delivery Booking**.\n" +
                                   "3. Enter pickup & drop-off locations, parcel weight, and vehicle.\n" +
                                   "4. View the instant AI cost calculation and confirm booking.\n" +
                                   "5. Once submitted, our Admin team assigns a nearby driver and vehicle!";
                }
                // F. Greetings
                else if (cleanText.Contains("hello") || cleanText.Contains("hi") || cleanText.Contains("hey") ||
                         cleanText.Contains("salam") || cleanText.Contains("aoa"))
                {
                    botReplyText = "👋 Hello! I am here to help. You can ask me:\n" +
                                   "• *\"Track FLT-2026-XXXXXX\"* to check order status\n" +
                                   "• *\"How is price calculated?\"*\n" +
                                   "• *\"What is the capacity of a Van or Truck?\"*\n" +
                                   "• *\"Talk to Admin\"* if you need human agent support.";
                }
                // G. Fallback
                else
                {
                    botReplyText = "🤖 I'm Fleetify's AI assistant. I can help answer common questions about tracking, rates, and fleet bookings.\n\n" +
                                   "If you have a complex inquiry or need special help, type **'Talk to Admin'** or click below, and an administrator will personally assist you!";
                }
            }

            var botMsg = new SupportMessage
            {
                ConversationID = conversationId,
                SenderType = "Bot",
                SenderName = "Fleetify AI Support",
                MessageText = botReplyText,
                SentAt = DateTime.UtcNow.AddSeconds(1)
            };
            _context.SupportMessages.Add(botMsg);
            await _context.SaveChangesAsync();

            return botMsg;
        }

        public async Task<SupportMessage> AddAdminReplyAsync(int conversationId, int adminId, string adminName, string replyMessage, bool resolve = false)
        {
            var conversation = await _context.SupportConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.ConversationID == conversationId);

            if (conversation == null)
            {
                throw new ArgumentException("Conversation not found.");
            }

            var adminMsg = new SupportMessage
            {
                ConversationID = conversationId,
                SenderType = "Admin",
                SenderName = $"{adminName} (Administrator)",
                MessageText = replyMessage.Trim(),
                SentAt = DateTime.UtcNow
            };

            _context.SupportMessages.Add(adminMsg);

            conversation.Status = resolve ? "Resolved" : "InProgress";
            conversation.UpdatedAt = DateTime.UtcNow;

            // Notify user if customer is registered
            if (conversation.UserID.HasValue)
            {
                await _notificationService.CreateNotificationAsync(
                    "Customer",
                    conversation.UserID.Value,
                    "Admin Replied to Support Ticket",
                    $"Admin {adminName} sent a reply: '{Truncate(replyMessage, 60)}'",
                    conversation.ConversationID
                );
            }

            await _context.SaveChangesAsync();
            return adminMsg;
        }

        public async Task<List<SupportConversation>> GetConversationsAsync(string? statusFilter = null)
        {
            var query = _context.SupportConversations
                .Include(c => c.Messages)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "All")
            {
                query = query.Where(c => c.Status == statusFilter);
            }

            return await query
                .OrderByDescending(c => c.Status == "NeedsAdmin")
                .ThenByDescending(c => c.UpdatedAt)
                .ToListAsync();
        }

        public async Task<SupportConversation?> GetConversationDetailsAsync(int conversationId)
        {
            return await _context.SupportConversations
                .Include(c => c.Messages.OrderBy(m => m.SentAt))
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.ConversationID == conversationId);
        }

        public async Task<bool> EscalateToAdminAsync(int conversationId, string? reason = null)
        {
            var conversation = await _context.SupportConversations.FindAsync(conversationId);
            if (conversation == null) return false;

            conversation.Status = "NeedsAdmin";
            conversation.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                conversation.Subject = $"Escalated: {Truncate(reason, 40)}";
            }

            var botMsg = new SupportMessage
            {
                ConversationID = conversationId,
                SenderType = "Bot",
                SenderName = "Fleetify AI Support",
                MessageText = "🚨 **Ticket Escalated!** You have requested human intervention. A Fleetify Administrator has been assigned and will reply here shortly.",
                SentAt = DateTime.UtcNow
            };
            _context.SupportMessages.Add(botMsg);

            var adminUserIds = await _context.Admins.Select(a => a.UserID).ToListAsync();
            foreach (var adminUserId in adminUserIds)
            {
                _context.Notifications.Add(new Notification
                {
                    RecipientType = "Admin",
                    RecipientID = adminUserId,
                    Title = "Support Ticket Escalated",
                    Message = $"Customer {conversation.UserName} requested admin help.",
                    SentAt = DateTime.UtcNow,
                    ReadStatus = "Unread",
                    RelatedEntityId = conversation.ConversationID
                });
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ResolveTicketAsync(int conversationId)
        {
            var conversation = await _context.SupportConversations.FindAsync(conversationId);
            if (conversation == null) return false;

            conversation.Status = "Resolved";
            conversation.UpdatedAt = DateTime.UtcNow;

            var botMsg = new SupportMessage
            {
                ConversationID = conversationId,
                SenderType = "Bot",
                SenderName = "Fleetify AI Support",
                MessageText = "✅ **Ticket Resolved.** This support session has been closed by the administrator. Thank you for choosing Fleetify!",
                SentAt = DateTime.UtcNow
            };
            _context.SupportMessages.Add(botMsg);

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> GetNeedsAdminCountAsync()
        {
            return await _context.SupportConversations.CountAsync(c => c.Status == "NeedsAdmin");
        }

        private static string Truncate(string val, int maxLength)
        {
            if (string.IsNullOrEmpty(val)) return string.Empty;
            return val.Length <= maxLength ? val : val.Substring(0, maxLength) + "...";
        }
    }
}
