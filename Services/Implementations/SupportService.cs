using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Fleetify.Data;
using Fleetify.Models.Common;
using Fleetify.Models.Entities;
using Fleetify.Services.Interfaces;

namespace Fleetify.Services.Implementations
{
    public class SupportService : ISupportService
    {
        private readonly FleetifyDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IGeminiService _geminiService;
        private readonly IMapRoutingService _mapRoutingService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;

        public SupportService(
            FleetifyDbContext context,
            INotificationService notificationService,
            IGeminiService geminiService,
            IMapRoutingService mapRoutingService,
            IEmailService emailService,
            IConfiguration config)
        {
            _context = context;
            _notificationService = notificationService;
            _geminiService = geminiService;
            _mapRoutingService = mapRoutingService;
            _emailService = emailService;
            _config = config;
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

                try
                {
                    _context.SupportConversations.Add(conversation);
                    await _context.SaveChangesAsync();
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException)
                {
                    _context.Entry(conversation).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                    conversation = await _context.SupportConversations
                        .Include(c => c.Messages)
                        .FirstOrDefaultAsync(c => c.SessionToken == sessionToken) ?? conversation;
                }
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

        public async Task<SupportMessage> ProcessUserMessageAsync(int conversationId, string userMessage, int? currentUserId = null, string? currentUserName = null)
        {
            var conversation = await _context.SupportConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.ConversationID == conversationId);

            if (conversation == null)
            {
                throw new ArgumentException("Conversation not found.");
            }

            // Sync user claims if authenticated
            if (currentUserId.HasValue && !conversation.UserID.HasValue)
            {
                conversation.UserID = currentUserId.Value;
            }
            if (!string.IsNullOrWhiteSpace(currentUserName) && (conversation.UserName == "Guest Customer" || string.IsNullOrWhiteSpace(conversation.UserName)))
            {
                conversation.UserName = currentUserName;
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

            // 2. Prepare Live Database & Map Context for AI
            string? liveContext = null;
            RouteDistanceResult? detectedMapRoute = null;

            // Check if user is inquiring about route distance / locations
            var routePair = ExtractRouteLocations(userMessage);
            if (routePair.HasValue)
            {
                try
                {
                    detectedMapRoute = await _mapRoutingService.GetDrivingDistanceAsync(routePair.Value.origin, routePair.Value.destination);
                    if (detectedMapRoute != null && detectedMapRoute.Success && detectedMapRoute.DistanceKm > 0)
                    {
                        var kgMatch = Regex.Match(userMessage, @"(\d+(\.\d+)?)\s*(kg|kilo|gram)", RegexOptions.IgnoreCase);
                        double parcelWt = kgMatch.Success && double.TryParse(kgMatch.Groups[1].Value, out double w) ? w : 1.0;
                        double distCharge = CostEstimationService.CalculateDistanceCharge(detectedMapRoute.DistanceKm);
                        double totalEst = Math.Floor((2.0 * parcelWt) + distCharge);

                        liveContext = $"=== LIVE MAP ROUTE & DISTANCE VERIFICATION ===\n" +
                                      $"• Origin (Pickup): {detectedMapRoute.Origin}\n" +
                                      $"• Destination (Dropoff): {detectedMapRoute.Destination}\n" +
                                      $"• Verified Driving Road Distance: {detectedMapRoute.DistanceKm} km\n" +
                                      $"• Estimated Travel Time: ~{detectedMapRoute.DurationMinutes} minutes\n" +
                                      $"• Routing Source: {detectedMapRoute.Provider}\n" +
                                      $"• Formula: Total Cost = ((2 × Weight_kg) + Distance_Tier_Charge) × Speed_Multiplier\n" +
                                      $"• Distance Tier Charge: Rs. {distCharge} (for {detectedMapRoute.DistanceKm} km)\n" +
                                      $"• Detected/Given Parcel Weight: {parcelWt:F1} kg (Weight fee: Rs. {2.0 * parcelWt:F0})\n" +
                                      $"• Standard (Normal) Calculation: (({2.0 * parcelWt:F0} + {distCharge}) × 1.0) = Rs. {totalEst}\n" +
                                      $"• Speed Multipliers: Normal = 1.0, Fast = 1.75, Express = 2.5";
                    }
                }
                catch { }
            }

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

                    string orderContext = $"Database Record for Tracking Code '{trackingNumber}':\n" +
                                          $"• Current Status: {order.RequestedStatus}\n" +
                                          $"• Route: {order.PickupLocation} -> {order.DropoffLocation}\n" +
                                          $"• Assigned Driver: {driverInfo}\n" +
                                          $"• Estimated Fare: ${order.EstimatedCost:F2}\n" +
                                          $"• Vehicle Type: {order.VehicleType}\n" +
                                          $"• Created At: {order.RequestDate:yyyy-MM-dd HH:mm} UTC";

                    liveContext = string.IsNullOrWhiteSpace(liveContext) ? orderContext : $"{liveContext}\n\n{orderContext}";
                }
                else
                {
                    string missingContext = $"Database search for tracking code '{trackingNumber}' returned NO MATCHING ORDER.";
                    liveContext = string.IsNullOrWhiteSpace(liveContext) ? missingContext : $"{liveContext}\n\n{missingContext}";
                }
            }
            else if (conversation.UserID.HasValue && detectedMapRoute == null)
            {
                var recentOrder = await _context.DeliveryRequests
                    .Where(d => d.UserID == conversation.UserID.Value)
                    .OrderByDescending(d => d.RequestDate)
                    .FirstOrDefaultAsync();

                if (recentOrder != null)
                {
                    string userHistContext = $"User's Most Recent Shipment Record: Tracking {recentOrder.TrackingNumber}, Status: {recentOrder.RequestedStatus}, Route: {recentOrder.PickupLocation} -> {recentOrder.DropoffLocation}, Cost: ${recentOrder.EstimatedCost:F2}";
                    liveContext = string.IsNullOrWhiteSpace(liveContext) ? userHistContext : $"{liveContext}\n\n{userHistContext}";
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
                // Deep, Contextual Natural Language Question Answering Engine
                botReplyText = await GenerateSmartBotReplyAsync(conversation, userMessage, trackingMatch, detectedMapRoute);
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

        public async Task<bool> EscalateToAdminAsync(int conversationId, string? reason = null, string? userMessage = null)
        {
            var conversation = await _context.SupportConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.ConversationID == conversationId);

            if (conversation == null) return false;

            conversation.Status = "NeedsAdmin";
            conversation.UpdatedAt = DateTime.UtcNow;

            // Extract the user's message
            string userMsgText = !string.IsNullOrWhiteSpace(userMessage)
                ? userMessage.Trim()
                : (!string.IsNullOrWhiteSpace(reason) &&
                   !reason.StartsWith("Inquiry flagged", StringComparison.OrdinalIgnoreCase) &&
                   !reason.StartsWith("User requested human", StringComparison.OrdinalIgnoreCase) &&
                   !reason.StartsWith("User requested admin", StringComparison.OrdinalIgnoreCase)
                    ? reason.Trim()
                    : conversation.Messages.Where(m => m.SenderType == "User").OrderByDescending(m => m.SentAt).Select(m => m.MessageText).FirstOrDefault()
                      ?? "User selected 'Contact Admin' option in the support desk.");

            if (!string.IsNullOrWhiteSpace(reason))
            {
                conversation.Subject = $"Escalated: {Truncate(reason, 40)}";
            }

            var botMsg = new SupportMessage
            {
                ConversationID = conversationId,
                SenderType = "Bot",
                SenderName = "Fleetify AI Support",
                MessageText = "🚨 **Ticket Escalated to Admin!** Your inquiry has been forwarded to our fleet operations administrator via email notification. An administrator will review your message and assist you shortly.",
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
                    Title = "Support: Contact Admin Request",
                    Message = $"Customer {conversation.UserName} sent: '{Truncate(userMsgText, 60)}'",
                    SentAt = DateTime.UtcNow,
                    ReadStatus = "Unread",
                    RelatedEntityId = conversation.ConversationID
                });
            }

            await _context.SaveChangesAsync();

            // Automatically send email notification to admin containing the user's message
            try
            {
                var allAdminEmails = new List<string>();

                // 1. Emails from Admins table
                var dbAdmins = await _context.Admins
                    .Where(a => !string.IsNullOrWhiteSpace(a.Email))
                    .Select(a => a.Email.Trim())
                    .ToListAsync();
                allAdminEmails.AddRange(dbAdmins);

                // 2. Emails from Users table where Role == 'Admin'
                var dbUserAdmins = await _context.Users
                    .Where(u => u.Role == "Admin" && !string.IsNullOrWhiteSpace(u.Email))
                    .Select(u => u.Email.Trim())
                    .ToListAsync();
                allAdminEmails.AddRange(dbUserAdmins);

                // 3. Always include the primary admin email explicitly
                if (!allAdminEmails.Contains("zainibutt242@gmail.com", StringComparer.OrdinalIgnoreCase))
                {
                    allAdminEmails.Add("zainibutt242@gmail.com");
                }

                // 4. Prioritize real deliverable email addresses, filter out fake/local placeholders
                var deliverableAdminEmails = allAdminEmails
                    .Where(e => !e.EndsWith("@fleetify.com", StringComparison.OrdinalIgnoreCase) &&
                                !e.EndsWith("@fleetify.local", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!deliverableAdminEmails.Any())
                {
                    deliverableAdminEmails = allAdminEmails.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }

                string emailSubject = $"[Fleetify Support] Contact Admin: Message from {conversation.UserName} (Ticket #{conversation.ConversationID})";

                var recentHistory = conversation.Messages
                    .OrderByDescending(m => m.SentAt)
                    .Take(5)
                    .OrderBy(m => m.SentAt)
                    .Select(m => $"• [{m.SenderName} - {m.SentAt:hh:mm tt}]: {m.MessageText}")
                    .ToList();

                string historyBlock = recentHistory.Any() ? string.Join("\n", recentHistory) : "No previous message history recorded.";

                string emailBody = $@"A user has selected the 'Contact Admin' option in the Fleetify Support Desk.

TICKET DETAILS:
• Ticket ID: #{conversation.ConversationID}
• Customer Name: {conversation.UserName}
• Customer Email: {conversation.UserEmail}
• Date & Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

USER'S MESSAGE:
------------------------------------------------------------
""{userMsgText}""
------------------------------------------------------------

RECENT SUPPORT CONTEXT:
{historyBlock}

ACTION REQUIRED:
Please log in to the Fleetify Admin Portal to reply to this customer ticket:
http://localhost:5000/Admin/Support?conversationId={conversation.ConversationID}";

                // Fire-and-forget in background Task so the HTTP endpoint returns in <10ms and the UI never freezes on 'Connecting...'
                _ = Task.Run(async () =>
                {
                    foreach (var email in deliverableAdminEmails)
                    {
                        try
                        {
                            await _emailService.SendEmailAsync(email, emailSubject, emailBody);
                        }
                        catch
                        {
                            // Handled by EmailService logger
                        }
                    }
                });
            }
            catch
            {
                // Ensure email dispatch failures do not block the support session
            }

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

                private async Task<string> GenerateSmartBotReplyAsync(
            SupportConversation conversation,
            string userMessage,
            Match trackingMatch,
            RouteDistanceResult? detectedMapRoute)
        {
            string clean = userMessage.ToLower().Trim();
            clean = Regex.Replace(clean, @"\s+", " ");

            // 1. City Compliment / Casual Remark about cities (e.g. "sheikho kitna payara ha", "lahore kaisa hai")
            bool mentionsCity = clean.Contains("sheikho") || clean.Contains("sheikhupura") || clean.Contains("lahore") ||
                                clean.Contains("karachi") || clean.Contains("islamabad") || clean.Contains("pindi") ||
                                clean.Contains("rawalpindi") || clean.Contains("gujranwala") || clean.Contains("faisalabad") ||
                                clean.Contains("multan") || clean.Contains("peshawar") || clean.Contains("sialkot") ||
                                clean.Contains("quetta") || clean.Contains("gujrat") || clean.Contains("sargodha") || clean.Contains("kasur");

            bool isCityPraiseOrCasual = mentionsCity && (clean.Contains("payara") || clean.Contains("pyara") ||
                                        clean.Contains("khoobsurat") || clean.Contains("khubsurat") ||
                                        clean.Contains("achha") || clean.Contains("acha") || clean.Contains("kaisa") ||
                                        clean.Contains("beautiful") || clean.Contains("kesa") || clean.Contains("best"));

            bool isExplicitPricing = clean.Contains("cost") || clean.Contains("price") || clean.Contains("rate") ||
                                     clean.Contains("fare") || clean.Contains("charge") || clean.Contains("kharcha") ||
                                     clean.Contains("pesay") || clean.Contains("rupay") || clean.Contains("pricing") ||
                                     clean.Contains("bill") || clean.Contains("kitne paise") || clean.Contains("kitna lagega") ||
                                     (clean.Contains("kitna") && (clean.Contains("hoga") || clean.Contains("lagega") || clean.Contains("rupee") || clean.Contains("rs")));

            if (isCityPraiseOrCasual && !isExplicitPricing)
            {
                string cityName = (clean.Contains("sheikho") || clean.Contains("sheikhupura")) ? "Sheikhupura (Sheikho)" :
                                  clean.Contains("lahore") ? "Lahore" :
                                  clean.Contains("karachi") ? "Karachi" :
                                  clean.Contains("islamabad") ? "Islamabad" :
                                  (clean.Contains("pindi") || clean.Contains("rawalpindi")) ? "Rawalpindi" :
                                  clean.Contains("gujranwala") ? "Gujranwala" :
                                  clean.Contains("faisalabad") ? "Faisalabad" :
                                  clean.Contains("multan") ? "Multan" :
                                  clean.Contains("peshawar") ? "Peshawar" :
                                  clean.Contains("sialkot") ? "Sialkot" :
                                  clean.Contains("quetta") ? "Quetta" : "Pakistan";

                return $"Haha, bilkul! **{cityName}** waqai bohat pyara aur ahem tareekhi shehar hai! â¤ï¸\n\n" +
                       $"Aur khushkhabri yeh hai ke **Fleetify** is shehar aur us ke gird-o-nawwah ke tamam ilaqon mein fast door-to-door courier aur commercial freight delivery provide karta hai.\n\n" +
                       $"Kya aap ko {cityName} se ya wahan koi parcel bhejna ya mangwana hai? Main aap ko foran rates aur delivery time bata sakta hoon! ðŸ˜Š";
            }

            // 2. Delivery Request / Booking Intent (e.g. "ma deliver request kasay dal sakta hon?", "booking kaise hoti hai")
            bool isBookingIntent = clean.Contains("deliver request") || clean.Contains("delivery request") ||
                                   clean.Contains("kasay dal") || clean.Contains("kaise dal") || clean.Contains("kesay dal") ||
                                   clean.Contains("kasay bhej") || clean.Contains("kaise bhej") || clean.Contains("kesay bhej") ||
                                   clean.Contains("parcel bhejna") || clean.Contains("order kaise") || clean.Contains("order kasay") ||
                                   clean.Contains("booking kaise") || clean.Contains("booking kasay") || clean.Contains("book kaise") ||
                                   clean.Contains("book kasay") || clean.Contains("bhejna hai") || clean.Contains("new booking") ||
                                   clean.Contains("how to book") || clean.Contains("send parcel") || clean.Contains("order book") ||
                                   clean.Contains("delivery book");

            if (isBookingIntent)
            {
                return "ðŸ“¦ **Fleetify Par Delivery Request Dalnay Ka Aasan Tareeqa:**\n\n" +
                       "1. **Login Karein:** Apne Customer Account mein sign in karein (`/Account/Login`).\n" +
                       "2. **New Booking:** Customer Dashboard par **'New Delivery Booking'** button dabayein.\n" +
                       "3. **Locations & Weight:** Pickup city/address, Drop-off address aur parcel ka wazan (kg) enter karein.\n" +
                       "4. **Vehicle & Speed:** Munasib gaari (Bike, Pickup, Van, ya Truck) aur speed (Normal ya Express) select karein.\n" +
                       "5. **Instant Cost:** Screen par verified road distance aur instant fare dekh kar **Confirm Booking** karein!\n\n" +
                       "Aap ki request submit hote hi hamara system qareebi driver assign kar dega aur live tracking shuru ho jaye gi! ðŸššâœ¨";
            }

            // 3. Order Tracking / My Shipment / Status
            bool isTrackingQuery = trackingMatch.Success || clean.Contains("flt-") || clean.Contains("track") ||
                                   clean.Contains("mera parcel") || clean.Contains("mera order") || clean.Contains("mera package") ||
                                   clean.Contains("meri delivery") || clean.Contains("shipment kahan") || clean.Contains("parcel kahan") ||
                                   clean.Contains("order kahan") || clean.Contains("kahan pohncha") || clean.Contains("kahan pohoncha") ||
                                   clean.Contains("kahan poncha") || clean.Contains("kab deliver") || clean.Contains("kab pohnche") ||
                                   clean.Contains("kab mile") || clean.Contains("order status") || clean.Contains("delivery status") ||
                                   clean.Contains("parcel status") || clean.Contains("where is my order") || clean.Contains("where is my parcel");

            if (isTrackingQuery)
            {
                string trackingNumber = trackingMatch.Success ? trackingMatch.Value : string.Empty;
                if (string.IsNullOrEmpty(trackingNumber))
                {
                    var fltInline = Regex.Match(userMessage.ToUpper(), @"FLT[A-Z0-9\-]+");
                    if (fltInline.Success) trackingNumber = fltInline.Value;
                }

                if (!string.IsNullOrEmpty(trackingNumber))
                {
                    var order = await _context.DeliveryRequests
                        .Include(d => d.Assignment)
                            .ThenInclude(a => a!.Driver)
                        .FirstOrDefaultAsync(d => d.TrackingNumber == trackingNumber);

                    if (order != null)
                    {
                        string driverInfo = order.Assignment?.Driver != null
                            ? $"{order.Assignment.Driver.FullName} (ðŸ“ž {order.Assignment.Driver.PhoneNumber})"
                            : "Awaiting Driver Dispatch";

                        string statusUrdu = order.RequestedStatus switch
                        {
                            "Pending" => "â³ Pending (Admin approval aur driver dispatch jari hai)",
                            "Assigned" => "ðŸšš Assigned (Driver assign ho chuka hai, pickup ke liye rawana)",
                            "In Transit" => "ðŸ›£ï¸ In Transit (Parcel raste mein hai aur jald delivery address par pohnche ga)",
                            "Delivered" => "âœ… Delivered (Kamyabi se deliver ho chuka hai)",
                            "Cancelled" => "âŒ Cancelled",
                            _ => order.RequestedStatus
                        };

                        return $"ðŸ“¦ **Shipment Found: `{order.TrackingNumber}`**\n\n" +
                               $"â€¢ **Current Status:** **{statusUrdu}**\n" +
                               $"â€¢ **Route:** {order.PickupLocation} âž” {order.DropoffLocation}\n" +
                               $"â€¢ **Assigned Driver:** {driverInfo}\n" +
                               $"â€¢ **Vehicle:** {order.VehicleType}\n" +
                               $"â€¢ **Fare:** Rs. {order.EstimatedCost:F0}\n" +
                               $"â€¢ **Date:** {order.RequestDate:dd MMM yyyy, hh:mm tt} UTC\n\n" +
                               $"Aap apne Customer Portal par is shipment ka live GPS route bhi dekh sakte hain!";
                    }
                    else
                    {
                        return $"ðŸ” Database mein tracking code **{trackingNumber}** ka koi active order nahi mila.\n\n" +
                               $"Barah-e-karam apna code dobara check karein (maslan `FLT-2026-302107`) ya apne Customer Dashboard par ja kar orders check karein.";
                    }
                }
                else if (conversation.UserID.HasValue)
                {
                    var userOrders = await _context.DeliveryRequests
                        .Include(d => d.Assignment)
                            .ThenInclude(a => a!.Driver)
                        .Where(d => d.UserID == conversation.UserID.Value)
                        .OrderByDescending(d => d.RequestDate)
                        .Take(2)
                        .ToListAsync();

                    if (userOrders.Any())
                    {
                        var latest = userOrders.First();
                        string driverInfo = latest.Assignment?.Driver != null
                            ? $"{latest.Assignment.Driver.FullName} (ðŸ“ž {latest.Assignment.Driver.PhoneNumber})"
                            : "Awaiting Driver Dispatch";

                        return $"ðŸ“¦ **Aap Ke Latest Order Ki Live Maloomat:**\n\n" +
                               $"â€¢ **Tracking Number:** `{latest.TrackingNumber}`\n" +
                               $"â€¢ **Status:** **{latest.RequestedStatus}**\n" +
                               $"â€¢ **Route:** {latest.PickupLocation} âž” {latest.DropoffLocation}\n" +
                               $"â€¢ **Driver:** {driverInfo}\n" +
                               $"â€¢ **Gaari (Vehicle):** {latest.VehicleType}\n" +
                               $"â€¢ **Fare:** Rs. {latest.EstimatedCost:F0}\n\n" +
                               $"Aap is order ka live tracking map apne **Customer Dashboard** par bhi dekh sakte hain!";
                    }
                    else
                    {
                        return "ðŸ“¦ Aap ke account par filhal koi active delivery request nazar nahi aa rahi.\n\n" +
                               "â€¢ Agar aap ke paas kisi specific parcel ka **Tracking Number** hai (maslan `FLT-2026-302107`), to woh yahan type karein.\n" +
                               "â€¢ Naya parcel book karne ke liye **Customer Dashboard âž” New Delivery Booking** par jayein.";
                    }
                }
                else
                {
                    return "ðŸ“¦ Parcel track karne ke liye apna **Tracking Code** (maslan `FLT-2026-302107`) yahan type karein.\n\n" +
                           "Ya apne **Customer Account** mein sign in karein jahan aap ke tamam orders ka live status aur driver GPS location show hota hai.";
                }
            }

            // 4. Driver / Rider Queries
            bool isDriverQuery = clean.Contains("driver") || clean.Contains("rider") || clean.Contains("delivery boy");
            if (isDriverQuery)
            {
                if (conversation.UserID.HasValue)
                {
                    var activeOrder = await _context.DeliveryRequests
                        .Include(d => d.Assignment)
                            .ThenInclude(a => a!.Driver)
                        .Where(d => d.UserID == conversation.UserID.Value && d.RequestedStatus != "Delivered" && d.RequestedStatus != "Cancelled")
                        .OrderByDescending(d => d.RequestDate)
                        .FirstOrDefaultAsync();

                    if (activeOrder?.Assignment?.Driver != null)
                    {
                        var drv = activeOrder.Assignment.Driver;
                        return $"ðŸšš **Aap Ke Assigned Driver Ki Details:**\n\n" +
                               $"â€¢ **Driver Name:** **{drv.FullName}**\n" +
                               $"â€¢ **Phone Number:** ðŸ“ž **{drv.PhoneNumber}**\n" +
                               $"â€¢ **Order Tracking:** `{activeOrder.TrackingNumber}`\n" +
                               $"â€¢ **Delivery Route:** {activeOrder.PickupLocation} âž” {activeOrder.DropoffLocation}\n\n" +
                               $"Aap is number par driver se seedha call kar ke rabta kar sakte hain ya live map par unki location dekh sakte hain!";
                    }
                    else if (activeOrder != null)
                    {
                        return $"ðŸšš Aap ka order `{activeOrder.TrackingNumber}` abhi driver assignment queue mein hai. Jald hi nazdeeki driver assign ho jaye ga aur unka direct phone number yahan show hoga.";
                    }
                }

                return "ðŸšš **Fleetify Drivers & Live Dispatch System:**\n\n" +
                       "Jab aap koi delivery request submit karte hain, to hamara system qareeb tareen verified driver assign karta hai. Driver ka naam, live moving location, aur direct mobile number aap ke **Customer Dashboard âž” Live Tracking** par nazar aata hai jahan se aap unhein direct call bhi kar sakte hain!";
            }

            // 5. Admin Escalation / Complaints / Urgent / Lost / Damaged
            bool isEscalation = clean.Contains("admin") || clean.Contains("human") || clean.Contains("agent") ||
                                clean.Contains("manager") || clean.Contains("representative") || clean.Contains("operator") ||
                                clean.Contains("shikayat") || clean.Contains("complaint") || clean.Contains("damaged") ||
                                clean.Contains("broken") || clean.Contains("refund") || clean.Contains("lost") ||
                                clean.Contains("urgent") || clean.Contains("fraud") || clean.Contains("dhoka") ||
                                clean.Contains("kharab") || clean.Contains("helpdesk");

            if (isEscalation)
            {
                await EscalateToAdminAsync(conversation.ConversationID, userMessage);
                return "ðŸš¨ **Connecting with Fleetify Administrator...**\n\n" +
                       "Aap ki inquiry ko **Human Administrator Intervention** ke liye mark kar diya gaya hai. Hamari fleet operations team ko email aur live notification send ho chuki hai. Administrator jald hi is chat thread mein review kar ke intervene karega.\n\n" +
                       "Aap mazeed details yahan bhej sakte hain ya hamari official helpline `fypfleetify@gmail.com` par email kar sakte hain.";
            }

            // 6. Pricing & Rate Calculation
            if (isExplicitPricing || detectedMapRoute != null)
            {
                if (detectedMapRoute != null && detectedMapRoute.Success)
                {
                    var kgMatch = Regex.Match(clean, @"(\d+(\.\d+)?)\s*(kg|kilo|gram)");
                    double wtVal = kgMatch.Success && double.TryParse(kgMatch.Groups[1].Value, out double w) ? w : 1.0;
                    double distCharge = CostEstimationService.CalculateDistanceCharge(detectedMapRoute.DistanceKm);
                    double normalCost = Math.Floor((2.0 * wtVal) + distCharge);
                    double fastCost = Math.Floor(((2.0 * wtVal) + distCharge) * 1.75);
                    double expressCost = Math.Floor(((2.0 * wtVal) + distCharge) * 2.5);

                    return $"ðŸ—ºï¸ **Verified Route Distance & Cost ({detectedMapRoute.Provider}):**\n\n" +
                           $"â€¢ **Pickup:** {detectedMapRoute.Origin}\n" +
                           $"â€¢ **Drop-off:** {detectedMapRoute.Destination}\n" +
                           $"â€¢ **Real Road Distance:** **{detectedMapRoute.DistanceKm} km** (~{detectedMapRoute.DurationMinutes} mins drive)\n" +
                           $"â€¢ **Package Weight:** {wtVal:F1} kg (Weight fee: Rs. {2.0 * wtVal:F0})\n" +
                           $"â€¢ **Distance Tier Charge:** Rs. {distCharge}\n\n" +
                           $"ðŸ’° **Calculated Delivery Fares:**\n" +
                           $"â€¢ ðŸ“¦ **Normal Delivery (4â€“6 din):** **Rs. {normalCost}**\n" +
                           $"â€¢ ðŸš€ **Fast Delivery (2â€“3 din):** **Rs. {fastCost}**\n" +
                           $"â€¢ âš¡ **Express Delivery (1â€“2 din):** **Rs. {expressCost}**\n\n" +
                           $"Aap foran Customer Portal par ja kar is route ki booking confirm kar sakte hain!";
                }
                else
                {
                    return "ðŸ’° **Fleetify Delivery Rates & Calculation Formula:**\n\n" +
                           "Hamara pricing model nihayat transparent hai:\n" +
                           "1. **Wazan (Weight):** Rs. 2 fee per kg (2 Ã— Total Weight)\n" +
                           "2. **Distance Tiers (Road Distance):**\n" +
                           "   â€¢ 50 km se kam: Rs. 1,500\n" +
                           "   â€¢ 150 km se kam: Rs. 2,500\n" +
                           "   â€¢ 250 km se kam: Rs. 4,000\n" +
                           "   â€¢ 500 km se kam: Rs. 7,000\n" +
                           "   â€¢ 1,000 km se kam: Rs. 12,000\n" +
                           "   â€¢ 1,000 km se zyada: Rs. 18,000\n" +
                           "3. **Speed Multipliers:** Normal = 1.0x | Fast = 1.75x | Express = 2.5x\n\n" +
                           "ðŸ‘‰ Aap apna **Pickup City**, **Drop-off City**, aur **Wazan (kg)** batayein (maslan: *'Lahore to Islamabad 5kg'*), main foran exact bill calculate kar ke bata doonga!";
                }
            }

            // 7. Transit Speed & Delivery Time
            bool isTimeQuery = clean.Contains("kitna time") || clean.Contains("kitne din") || clean.Contains("kab pohnche") ||
                               clean.Contains("delivery time") || clean.Contains("express delivery") || clean.Contains("fast delivery") ||
                               clean.Contains("transit time") || clean.Contains("speed") || clean.Contains("urgently") ||
                               clean.Contains("emergency") || clean.Contains("same day") || clean.Contains("kitni der");

            if (isTimeQuery)
            {
                return "â±ï¸ **Fleetify Delivery Timings & Speeds:**\n\n" +
                       "â€¢ âš¡ **Express Delivery:** 1 se 2 business days (Urgent aur emergency shipments ke liye, priority routing)\n" +
                       "â€¢ ðŸš€ **Fast Delivery:** 2 se 3 business days (Standard se tez delivery)\n" +
                       "â€¢ ðŸ“¦ **Normal Delivery:** 4 se 6 business days (Standard economical rate)\n\n" +
                       "Delivery fleet Monday to Saturday active rehti hai aur live GPS tracking 24/7 online dastiab hai!";
            }

            // 8. Vehicles & Capacities
            bool isVehicleQuery = clean.Contains("vehicle") || clean.Contains("gaari") || clean.Contains("bike") ||
                                  clean.Contains("pickup") || clean.Contains("van") || clean.Contains("truck") ||
                                  clean.Contains("capacity") || clean.Contains("wazan limit") || clean.Contains("heavy cargo") ||
                                  clean.Contains("kitna wazan") || clean.Contains("motorcycle");

            if (isVehicleQuery)
            {
                return "ðŸšš **Fleetify Vehicles & Wazan (Capacity) Limits:**\n\n" +
                       "â€¢ ðŸï¸ **Bike:** 40 kg tak (Envelopes, legal documents, food aur chotay parcels)\n" +
                       "â€¢ ðŸ›» **Pickup:** 800 kg tak (Tools, equipment, hardware aur medium freight)\n" +
                       "â€¢ ðŸš **Delivery Van:** 1,500 kg tak (Cartons, appliances, mid-sized commercial freight)\n" +
                       "â€¢ ðŸš› **Heavy Truck:** 5,000 kg tak (Industrial machinery, pallets aur bulk cargo)\n\n" +
                       "Booking ke waqt aap ke wazan ke mutabiq best vehicle automatically suggest ho jati hai!";
            }

            // 9. Offices, Hubs, Warehouses & Cities
            bool isLocationQuery = clean.Contains("office") || clean.Contains("head office") || clean.Contains("branch") ||
                                   clean.Contains("warehouse") || clean.Contains("hubs") || clean.Contains("hub") ||
                                   clean.Contains("cities") || clean.Contains("kahan kahan service") || clean.Contains("address") ||
                                   clean.Contains("location") || clean.Contains("markaz");

            if (isLocationQuery)
            {
                return "ðŸ“ **Fleetify Network, Hubs & Warehouses:**\n\n" +
                       "Fleetify Pakistan ke tamam baray commercial marakiz mein faal hai:\n" +
                       "â€¢ ðŸ¢ **Head Office & Main Hub:** Shaheenabad, near Gulshan Park / GT Road, **Gujranwala**\n" +
                       "â€¢ ðŸ›ï¸ **Lahore Hub:** Thokar Niaz Baig, Multan Road Bypass\n" +
                       "â€¢ ðŸ›ï¸ **Islamabad Hub:** Sector I-9 Industrial Area\n" +
                       "â€¢ ðŸ›ï¸ **Rawalpindi Hub:** Murree Road / Faizabad Interchange\n" +
                       "â€¢ ðŸ›ï¸ **Faisalabad Hub:** Sargodha Road, Millat Chowk\n" +
                       "â€¢ ðŸ›ï¸ **Multan Hub:** Khanewal Road Industrial Estate\n" +
                       "â€¢ ðŸ›ï¸ **Peshawar Hub:** Hayatabad Industrial Estate, Phase 7\n" +
                       "â€¢ ðŸ›ï¸ **Sialkot Hub:** Wazirabad Road / Sambrial\n" +
                       "â€¢ ðŸ›ï¸ **Karachi Hub:** S.I.T.E. Industrial Area, Habib Bank Chowrangi\n" +
                       "â€¢ ðŸ›ï¸ **Quetta Hub:** Zarghoon Road Cantt\n\n" +
                       "Tamam shehron mein **Doorstep Pickup** aur **Doorstep Delivery** ki mukammal sahulat dastiab hai!";
            }

            // 10. Payment Methods / COD
            bool isPaymentQuery = clean.Contains("cash on delivery") || clean.Contains("cod") || clean.Contains("payment") ||
                                  clean.Contains("pesay kaise") || clean.Contains("paise kaise") || clean.Contains("card payment") ||
                                  clean.Contains("bank transfer") || clean.Contains("online pay") || clean.Contains("pesay kab");

            if (isPaymentQuery)
            {
                return "ðŸ’³ **Fleetify Payment Methods:**\n\n" +
                       "â€¢ ðŸ’µ **Cash on Delivery (COD):** Aap parcel received hone par driver ko naqad (cash) ada kar sakte hain.\n" +
                       "â€¢ ðŸ¦ **Direct Bank Transfer:** Online bank account ke zariye digital payment.\n" +
                       "â€¢ ðŸ’³ **Debit / Credit Card:** Booking ke waqt secure card checkout.\n\n" +
                       "Har delivery ki digital receipt aap ke Customer Dashboard par mehfooz rehti hai.";
            }

            // 11. Account, Login, Password & Registration
            bool isAccountQuery = clean.Contains("login") || clean.Contains("sign in") || clean.Contains("password") ||
                                  clean.Contains("forgot password") || clean.Contains("register") || clean.Contains("account") ||
                                  clean.Contains("bhool gya") || clean.Contains("bhool gaya") || clean.Contains("otp") ||
                                  clean.Contains("admin login");

            if (isAccountQuery)
            {
                return "ðŸ” **Fleetify Account & Login Rahnumai:**\n\n" +
                       "â€¢ **Customer Login:** `/Account/Login` par apna email aur password enter karein.\n" +
                       "â€¢ **Forgot Password:** Login page par **'Forgot Password?'** par click karein. Aap ke registered email par foran OTP reset code bhej diya jaye ga.\n" +
                       "â€¢ **Naya Account:** Naya Customer account banane ke liye `/Account/Register` par jayein.\n" +
                       "â€¢ **Admin Login:** Agar aap Administrator hain, to login page ke bilkul nichay **'ðŸ›¡ï¸ Sign in as an Admin'** link par click karein (`/Account/Login?role=Admin`).";
            }

            // 12. Cancellation & Modifications
            bool isCancelQuery = clean.Contains("cancel") || clean.Contains("radd") || clean.Contains("tabdeel") ||
                                 clean.Contains("change address") || clean.Contains("pata badal") || clean.Contains("return") ||
                                 clean.Contains("wapsi");

            if (isCancelQuery)
            {
                return "ðŸ”„ **Order Cancellation & Address Changes:**\n\n" +
                       "â€¢ **Cancel:** Jab tak order 'Pending' ya 'Assigned' status mein ho, aap **Customer Dashboard âž” My Orders** se direct cancel kar sakte hain.\n" +
                       "â€¢ **Address Change:** Agar dispatch ho chuka ho ya address update karna ho, to foran yahan **'Contact Admin'** button dabayein taake hamari team driver ko naya drop-off communicate kar sakay.";
            }

            // 13. Identity & Platform Description
            bool isIdentityQuery = clean.Contains("tum kon ho") || clean.Contains("who are you") || clean.Contains("kaun ho") ||
                                   clean.Contains("what is fleetify") || clean.Contains("fleetify kya hai") || clean.Contains("fleetify kia hai") ||
                                   clean.Contains("app kis cheez") || clean.Contains("website kya karti") || clean.Contains("tumhara naam") ||
                                   clean.Contains("aap ka naam");

            if (isIdentityQuery)
            {
                return "Main **Fleetify Virtual AI Assistant** hoon! ðŸš›âœ¨\n\n" +
                       "**Fleetify** Pakistan ka smart digital logistics aur fleet management platform hai. Hum pooray mulk mein tez aur safe parcel deliveries, live GPS driver tracking, aur cargo freight provide karte hain.\n\nAap mujh se tracking status, delivery rates, vehicle booking, ya administration support ke baray mein kuch bhi pooch sakte hain!";
            }

            // 14. Greetings & Casual Well-being
            bool isGreeting = clean.Contains("salam") || clean.Contains("aoa") || clean.Contains("assalam") ||
                              clean.Contains("asalam") || clean.Contains("hello") || clean.Contains("hi") ||
                              clean.Contains("hey") || clean.Contains("kya hal") || clean.Contains("kia hal") ||
                              clean.Contains("kaise ho") || clean.Contains("kese ho") || clean.Contains("how are you") ||
                              clean.Contains("mizaj") || clean.Contains("mizaaj") || clean.Contains("whats up") || clean.Contains("kya chal");

            if (isGreeting)
            {
                return "Walaikum Assalam! Alhamdulillah main bilkul theek hoon. Main Fleetify ka AI Assistant hoon. ðŸ˜Š\n\n" +
                       "Aap sunayein, aaj aap ki parcel delivery, cost calculation ya booking ke silsilay mein kya madad kar sakta hoon? Aap mujh se parcel track karne, rates maloom karne, ya booking ka tareeqa pooch sakte hain!";
            }

            // 15. Gratitude & Politeness
            bool isThanks = clean.Contains("shukriya") || clean.Contains("thanks") || clean.Contains("thank you") ||
                            clean.Contains("jazakallah") || clean.Contains("jazak allah") || clean.Contains("zabardast") ||
                            clean.Contains("good job") || clean.Contains("great");

            if (isThanks)
            {
                return "Bohat shukriya! Khushi hui ke main aap ki madad kar saka. Agar koi aur sawaal ho ya delivery book karni ho to bejhiijhak poochein. Fleetify aap ki khidmat ke liye hamesha tayyar hai! ðŸ˜Šâœ¨";
            }

            // 16. Acknowledgements
            bool isAck = clean == "ok" || clean == "okay" || clean == "theek hai" || clean == "acha" ||
                         clean == "achha" || clean == "sahi" || clean == "thk hai" || clean == "jee" ||
                         clean == "ji" || clean.StartsWith("ok ") || clean.StartsWith("theek hai");

            if (isAck)
            {
                return "Ji behtareen! Agar koi aur sawaal ya help chahiye ho to zaroor batayein.";
            }

            // 17. Fragile, Packaging, Sundays
            if (clean.Contains("fragile") || clean.Contains("toot") || clean.Contains("glass") || clean.Contains("sheesha"))
            {
                return "ðŸ›¡ï¸ **Fragile Items Handling:**\n\nFleetify fragile saman (sheesha, electronics, sensitive cargo) ke liye safe handling provide karta hai. Booking karte waqt description mein zaroor likhein taake driver extra care aur secure placement kare.";
            }

            if (clean.Contains("packaging") || clean.Contains("dabba") || clean.Contains("box") || clean.Contains("pack"))
            {
                return "ðŸ“¦ **Packaging Guidelines:**\n\nParcels ko standard carton box ya bubble wrap mein pack karna behtar hota hai. Chotay parcels ke liye waterproof packaging use karein.";
            }

            if (clean.Contains("sunday") || clean.Contains("itwar") || clean.Contains("timing") || clean.Contains("hours") || clean.Contains("khula"))
            {
                return "ðŸ•’ **Timings & Operations:**\n\nFleetify delivery fleet Monday to Saturday subah 8 baje se raat 8 baje tak operate karti hai. System booking aur AI support desk 24 ghante (24/7) online faal rehti hai.";
            }

            // 18. Contextual Intelligent Fallback
            return $"Aap ke sawaal ke mutabiq, Fleetify logistics platform par hum har qisam ki fast courier delivery, freight logistics aur parcel tracking provide karte hain.\n\n" +
                   $"â€¢ Agar aap kisi parcel ka status check karna chahte hain, to **Tracking Code** (maslan `FLT-2026-302107`) yahan likhein.\n" +
                   $"â€¢ Agar kisi route ki delivery cost janni hai, to **Pickup City**, **Drop-off City** aur **Wazan (kg)** batayein.\n" +
                   $"â€¢ Agar aap ko kisi khas maslay mein human assistance chahiye, to **'Contact Admin'** par click karein aur hamara administrator foran aap ki madad karega! ðŸ˜Š";
        }

        private (string origin, string destination)? ExtractRouteLocations(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;

            // 1. "from {origin} to {destination}"
            var fromTo = Regex.Match(message, @"from\s+([A-Za-z0-9\s,]+?)\s+to\s+([A-Za-z0-9\s,]+?)($|\?|\.|\s*(?:parcel|deliver|cost|price|rate|wazan|kg|km))", RegexOptions.IgnoreCase);
            if (fromTo.Success && !string.IsNullOrWhiteSpace(fromTo.Groups[1].Value) && !string.IsNullOrWhiteSpace(fromTo.Groups[2].Value))
            {
                return (fromTo.Groups[1].Value.Trim(), fromTo.Groups[2].Value.Trim());
            }

            // 2. "{origin} (se|sa) {destination} (tak|cheez|parcel|jana|jani|bhejna|le kar|la kar)"
            var seTak = Regex.Match(message, @"([A-Za-z0-9\s,]+?)\s+(?:se|sa)\s+([A-Za-z0-9\s,]+?)(?:\s+(?:tak|cheez|parcel|jana|jani|bhejna|le\s+kar|la\s+kar|jaana|bheja)|$|\?|\.)", RegexOptions.IgnoreCase);
            if (seTak.Success && !string.IsNullOrWhiteSpace(seTak.Groups[1].Value) && !string.IsNullOrWhiteSpace(seTak.Groups[2].Value))
            {
                string o = seTak.Groups[1].Value.Trim();
                string d = seTak.Groups[2].Value.Trim();
                o = Regex.Replace(o, @"^(?:main|mai|hum|muje|mujhe|ma)\s+(?:ne\s+|na\s+)?", "", RegexOptions.IgnoreCase).Trim();
                if (o.Length > 2 && d.Length > 2)
                {
                    return (o, d);
                }
            }

            // 3. "{origin} to {destination}"
            var toMatch = Regex.Match(message, @"([A-Za-z0-9\s,]+?)\s+to\s+([A-Za-z0-9\s,]+?)($|\?|\.|\s*(?:cost|price|rate|wazan|kg|km))", RegexOptions.IgnoreCase);
            if (toMatch.Success && !string.IsNullOrWhiteSpace(toMatch.Groups[1].Value) && !string.IsNullOrWhiteSpace(toMatch.Groups[2].Value))
            {
                string o = toMatch.Groups[1].Value.Trim();
                string d = toMatch.Groups[2].Value.Trim();
                if (o.Length > 2 && d.Length > 2 && !o.Equals("talk", StringComparison.OrdinalIgnoreCase))
                {
                    return (o, d);
                }
            }

            // 4. Known Pakistani City Pairs in text
            var knownCities = new[] { "gujranwala", "lahore", "islamabad", "rawalpindi", "pindi", "faisalabad", "multan", "peshawar", "sialkot", "karachi", "quetta", "sheikhupura", "sheikho", "gujrat", "kasur", "sargodha" };
            string lower = message.ToLower();
            string? firstCity = null;
            string? secondCity = null;
            int firstIdx = int.MaxValue;
            int secondIdx = int.MaxValue;

            foreach (var city in knownCities)
            {
                int idx = lower.IndexOf(city);
                if (idx >= 0)
                {
                    if (idx < firstIdx)
                    {
                        secondCity = firstCity;
                        secondIdx = firstIdx;
                        firstCity = city;
                        firstIdx = idx;
                    }
                    else if (idx < secondIdx && city != firstCity)
                    {
                        secondCity = city;
                        secondIdx = idx;
                    }
                }
            }

            if (!string.IsNullOrEmpty(firstCity) && !string.IsNullOrEmpty(secondCity) && firstCity != secondCity)
            {
                string mapCity(string c) => c switch
                {
                    "sheikho" => "Sheikhupura",
                    "sheikhupura" => "Sheikhupura",
                    "pindi" => "Rawalpindi",
                    _ => char.ToUpper(c[0]) + c.Substring(1)
                };
                return (mapCity(firstCity), mapCity(secondCity));
            }

            return null;
        }
    }
}
