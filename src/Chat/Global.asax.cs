using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Funq;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Configuration;
using ServiceStack.Redis;
using ServiceStack.Script;

namespace Chat
{
    public partial class AppHost : AppHostBase
    {
        public AppHost() : base("Chat", typeof (ServerEventsServices).Assembly) {}

        public static void Load() => PreInit();
        static partial void PreInit();
        static partial void PreConfigure(IAppHost appHost);

        public override void Configure(Container container)
        {
            PreConfigure(this);

            Plugins.Add(new SharpPagesFeature());
            Plugins.Add(new ServerEventsFeature());
            SetConfig(new HostConfig {
                DefaultContentType = MimeTypes.Json,
                AllowSessionIdsInHttpParams = true,
                UseCamelCase = true,
            });
            this.CustomErrorHttpHandlers.Remove(HttpStatusCode.Forbidden);

            //Register all Authentication methods you want to enable for this web app.            
            Plugins.Add(new AuthFeature(
                () => new AuthUserSession(),
                new IAuthProvider[] {
                    new TwitterAuthProvider(AppSettings),   //Sign-in with Twitter
                    new FacebookAuthProvider(AppSettings),  //Sign-in with Facebook
                    new GithubAuthProvider(AppSettings),    //Sign-in with GitHub OAuth Provider
                    new GoogleAuthProvider(AppSettings),    //Sign-in with Google OAuth Provider
                }));

            container.RegisterAutoWiredAs<MemoryChatHistory, IChatHistory>();

            var redisHost = AppSettings.GetString("RedisHost");
            if (redisHost != null)
            {
                container.Register<IRedisClientsManager>(new RedisManagerPool(redisHost));

                container.Register<IServerEvents>(c =>
                    new RedisServerEvents(c.Resolve<IRedisClientsManager>()));
                container.Resolve<IServerEvents>().Start();
            }

            // for lte IE 9 support + allow connections from local web dev apps
            Plugins.Add(new CorsFeature(
                allowOriginWhitelist: new[] { "http://localhost", "http://127.0.0.1:8080", "http://localhost:8080", "http://localhost:8081", "http://null.jsbin.com" },
                allowCredentials: true,
                allowedHeaders: "Content-Type, Allow, Authorization"));
        }
    }

    public interface IChatHistory
    {
        long GetNextMessageId(string channel);

        void Log(string channel, ChatMessage msg);

        List<ChatMessage> GetRecentChatHistory(string channel, long? afterId, int? take);

        void Flush();
    }

    public class MemoryChatHistory : IChatHistory
    {
        public int DefaultLimit { get; set; }

        public IServerEvents ServerEvents { get; set; }

        public MemoryChatHistory()
        {
            DefaultLimit = 100;
        }

        Dictionary<string, List<ChatMessage>> MessagesMap = new Dictionary<string, List<ChatMessage>>();

        public long GetNextMessageId(string channel)
        {
            return ServerEvents.GetNextSequence("chatMsg");
        }

        public void Log(string channel, ChatMessage msg)
        {
            if (!MessagesMap.TryGetValue(channel, out var msgs))
                MessagesMap[channel] = msgs = new List<ChatMessage>();

            msgs.Add(msg);
        }

        public List<ChatMessage> GetRecentChatHistory(string channel, long? afterId, int? take)
        {
            if (!MessagesMap.TryGetValue(channel, out var msgs))
                return new List<ChatMessage>();

            var ret = msgs.Where(x => x.Id > afterId.GetValueOrDefault())
                          .Reverse()  //get latest logs
                          .Take(take.GetValueOrDefault(DefaultLimit))
                          .Reverse(); //reverse back

            return ret.ToList();
        }

        public void Flush()
        {
            MessagesMap = new Dictionary<string, List<ChatMessage>>();
        }
    }

    [Route("/channels/{Channel}/chat")]
    public class PostChatToChannel : IReturn<ChatMessage>
    {
        public string From { get; set; }
        public string ToUserId { get; set; }
        public string Channel { get; set; }
        public string Message { get; set; }
        public string Selector { get; set; }
    }

    public class ChatMessage
    {
        public long Id { get; set; }
        public string Channel { get; set; }
        public string FromUserId { get; set; }
        public string FromName { get; set; }
        public string DisplayName { get; set; }
        public string Message { get; set; }
        public string UserAuthId { get; set; }
        public bool Private { get; set; }
    }

    [Route("/channels/{Channel}/raw")]
    public class PostRawToChannel : IReturnVoid
    {
        public string From { get; set; }
        public string ToUserId { get; set; }
        public string Channel { get; set; }
        public string Message { get; set; }
        public string Selector { get; set; }
    }

    [Route("/chathistory")]
    public class GetChatHistory : IReturn<GetChatHistoryResponse>
    {
        public string[] Channels { get; set; }
        public long? AfterId { get; set; }
        public int? Take { get; set; }
    }

    public class GetChatHistoryResponse
    {
        public List<ChatMessage> Results { get; set; }
        public ResponseStatus ResponseStatus { get; set; }
    }

    [Route("/reset")]
    public class ClearChatHistory : IReturnVoid { }

    [Route("/reset-serverevents")]
    public class ResetServerEvents : IReturnVoid { }

    [Route("/channels/{Channel}/object")]
    public class PostObjectToChannel : IReturnVoid
    {
        public string ToUserId { get; set; }
        public string Channel { get; set; }
        public string Selector { get; set; }

        public CustomType CustomType { get; set; }
        public SetterType SetterType { get; set; }
    }
    public class CustomType
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
    public class SetterType
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class ServerEventsServices : Service
    {
        public IServerEvents ServerEvents { get; set; }
        public IChatHistory ChatHistory { get; set; }
        public IAppSettings AppSettings { get; set; }

        public async Task Any(PostRawToChannel request)
        {
            if (!IsAuthenticated && AppSettings.Get("LimitRemoteControlToAuthenticatedUsers", false))
                throw new HttpError(HttpStatusCode.Forbidden, "You must be authenticated to use remote control.");

            // Ensure the subscription sending this notification is still active
            var sub = ServerEvents.GetSubscriptionInfo(request.From);
            if (sub == null)
                throw HttpError.NotFound($"Subscription {request.From} does not exist");

            // Check to see if this is a private message to a specific user
            var msg = request.Message?.HtmlEncode();
            if (request.ToUserId != null)
            {
                // Only notify that specific user
                await ServerEvents.NotifyUserIdAsync(request.ToUserId, request.Selector, msg);
            }
            else
            {
                // Notify everyone in the channel for public messages
                await ServerEvents.NotifyChannelAsync(request.Channel, request.Selector, msg);
            }
        }

        public async Task<object> Any(PostChatToChannel request)
        {
            // Ensure the subscription sending this notification is still active
            var sub = ServerEvents.GetSubscriptionInfo(request.From);
            if (sub == null)
                throw HttpError.NotFound("Subscription {0} does not exist".Fmt(request.From));

            var channel = request.Channel;

            var chatMessage = request.Message.IndexOf("{{", StringComparison.Ordinal) >= 0
                ? await HostContext.AppHost.ScriptContext.RenderScriptAsync(request.Message, new Dictionary<string, object> {
                    [nameof(Request)] = Request
                })
                : request.Message;

            // Create a DTO ChatMessage to hold all required info about this message
            var msg = new ChatMessage
            {
                Id = ChatHistory.GetNextMessageId(channel),
                Channel = request.Channel,
                FromUserId = sub.UserId,
                FromName = sub.DisplayName,
                Message = chatMessage.HtmlEncode(),
            };

            // Check to see if this is a private message to a specific user
            if (request.ToUserId != null)
            {
                // Mark the message as private so it can be displayed differently in Chat
                msg.Private = true;
                // Send the message to the specific user Id
                await ServerEvents.NotifyUserIdAsync(request.ToUserId, request.Selector, msg);

                // Also provide UI feedback to the user sending the private message so they
                // can see what was sent. Relay it to all senders active subscriptions 
                var toSubs = ServerEvents.GetSubscriptionInfosByUserId(request.ToUserId);
                foreach (var toSub in toSubs)
                {
                    // Change the message format to contain who the private message was sent to
                    msg.Message = $"@{toSub.DisplayName}: {msg.Message}";
                    await ServerEvents.NotifySubscriptionAsync(request.From, request.Selector, msg);
                }
            }
            else
            {
                // Notify everyone in the channel for public messages
                await ServerEvents.NotifyChannelAsync(request.Channel, request.Selector, msg);
            }

            if (!msg.Private)
                ChatHistory.Log(channel, msg);

            return msg;
        }

        public object Any(GetChatHistory request)
        {
            var msgs = request.Channels.Map(x =>
                ChatHistory.GetRecentChatHistory(x, request.AfterId, request.Take))
                .SelectMany(x => x)
                .OrderBy(x => x.Id)
                .ToList();

            return new GetChatHistoryResponse
            {
                Results = msgs
            };
        }

        public object Any(ClearChatHistory request)
        {
            ChatHistory.Flush();
            return HttpResult.Redirect("/");
        }

        public void Any(ResetServerEvents request)
        {
            ServerEvents.Reset();
        }

        public async Task Any(PostObjectToChannel request)
        {
            if (request.ToUserId != null)
            {
                if (request.CustomType != null)
                    await ServerEvents.NotifyUserIdAsync(request.ToUserId, request.Selector ?? Selector.Id<CustomType>(), request.CustomType);
                if (request.SetterType != null)
                    await ServerEvents.NotifyUserIdAsync(request.ToUserId, request.Selector ?? Selector.Id<SetterType>(), request.SetterType);
            }
            else
            {
                if (request.CustomType != null)
                    await ServerEvents.NotifyChannelAsync(request.Channel, request.Selector ?? Selector.Id<CustomType>(), request.CustomType);
                if (request.SetterType != null)
                    await ServerEvents.NotifyChannelAsync(request.Channel, request.Selector ?? Selector.Id<SetterType>(), request.SetterType);
            }
        }
    }

    public class Global : System.Web.HttpApplication
    {
        protected void Application_Start(object sender, EventArgs e)
        {
            new AppHost().Init();
        }
    }

    [Route("/account")]
    public class GetUserDetails {}

    public class GetUserDetailsResponse
    {
        public string Provider { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string FullName { get; set; }
        public string DisplayName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Company { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }

        public DateTime? BirthDate { get; set; }
        public string BirthDateRaw { get; set; }
        public string Address { get; set; }
        public string Address2 { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public string Culture { get; set; }
        public string Gender { get; set; }
        public string Language { get; set; }
        public string MailAddress { get; set; }
        public string Nickname { get; set; }
        public string PostalCode { get; set; }
        public string TimeZone { get; set; }
    }

    [Authenticate]
    public class UserDetailsService : Service
    {
        public object Get(GetUserDetails request)
        {
            var session = GetSession();
            return session.ConvertTo<GetUserDetailsResponse>();
        }
    }
}