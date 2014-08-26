using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Funq;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Configuration;
using ServiceStack.Razor;
using ServiceStack.Text;

namespace Chat
{
    public class AppHost : AppHostBase
    {
        public AppHost() : base("Chat", typeof(ServerEventsServices).Assembly) {}

        public override void Configure(Container container)
        {
            JsConfig.EmitCamelCaseNames = true;
            var liveSettings = "~/appsettings.txt".MapHostAbsolutePath();
            var appSettings = File.Exists(liveSettings)
                ? (IAppSettings)new TextFileSettings(liveSettings)
                : new AppSettings();

            Plugins.Add(new RazorFormat());
            Plugins.Add(new ServerEventsFeature());
            SetConfig(new HostConfig { DefaultContentType = MimeTypes.Json });
            this.CustomErrorHttpHandlers.Remove(HttpStatusCode.Forbidden);

            //Register all Authentication methods you want to enable for this web app.            
            Plugins.Add(new AuthFeature(
                () => new AuthUserSession(),
                new IAuthProvider[] {
                    new TwitterAuthProvider(appSettings),   //Sign-in with Twitter
                    new FacebookAuthProvider(appSettings),  //Sign-in with Facebook
                    new GithubAuthProvider(appSettings),    //Sign-in with GitHub OAuth Provider
                }));

            container.RegisterAutoWiredAs<MemoryChatHistory, IChatHistory>();
        }
    }

    public interface IChatHistory
    {
        long GetNextMessageId(string channel);

        void Log(string channel, ChatMessage msg);

        List<ChatMessage> GetRecentChatHistory(string channel, long? afterId, int? take);
    }

    public class MemoryChatHistory : IChatHistory
    {
        public int DefaultLimit { get; set; }

        public MemoryChatHistory()
        {
            DefaultLimit = 100;
        }

        private long messageId; 

        Dictionary<string, List<ChatMessage>> MessagesMap = new Dictionary<string, List<ChatMessage>>();

        public long GetNextMessageId(string channel)
        {
            return Interlocked.Increment(ref messageId);
        }

        public void Log(string channel, ChatMessage msg)
        {
            List<ChatMessage> msgs;
            if (!MessagesMap.TryGetValue(channel, out msgs))
                MessagesMap[channel] = msgs = new List<ChatMessage>();

            msgs.Add(msg);
        }

        public List<ChatMessage> GetRecentChatHistory(string channel, long? afterId, int? take)
        {
            List<ChatMessage> msgs;
            if (!MessagesMap.TryGetValue(channel, out msgs))
                return new List<ChatMessage>();

            var ret = msgs.Where(x => x.Id > afterId.GetValueOrDefault())
                          .Reverse()  //get latest logs
                          .Take(take.GetValueOrDefault(DefaultLimit))
                          .Reverse(); //reverse back
 
            return ret.ToList();
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

    [Route("/channels/{Channel}/history")]
    public class GetChatHistory : IReturn<GetChatHistoryResponse>
    {
        public string Channel { get; set; }
        public long? AfterId { get; set; }
        public int? Take { get; set; }
    }

    public class GetChatHistoryResponse
    {
        public List<ChatMessage> Results { get; set; }
        public ResponseStatus ResponseStatus { get; set; }
    }


    public class ServerEventsServices : Service
    {
        public IServerEvents ServerEvents { get; set; }
        public IChatHistory ChatHistory { get; set; }

        public void Any(PostRawToChannel request)
        {
            if (!IsAuthenticated)
                throw new HttpError(HttpStatusCode.Forbidden, "You must be authenticated to use remote control.");

            // Ensure the subscription sending this notification is still active
            var sub = ServerEvents.GetSubscription(request.From);
            if (sub == null)
                throw HttpError.NotFound("Subscription {0} does not exist".Fmt(request.From));

            // Check to see if this is a private message to a specific user
            if (request.ToUserId != null)
            {
                // Only notify that specific user
                ServerEvents.NotifyUserId(request.ToUserId, request.Selector, request.Message);
            }
            else
            {
                // Notify everyone in the channel for public messages
                ServerEvents.NotifyChannel(request.Channel, request.Selector, request.Message);
            }
        }

        public object Any(PostChatToChannel request)
        {
            // Ensure the subscription sending this notification is still active
            var sub = ServerEvents.GetSubscription(request.From);
            if (sub == null)
                throw HttpError.NotFound("Subscription {0} does not exist".Fmt(request.From));

            var channel = request.Channel;

            // Create a DTO ChatMessage to hold all required info about this message
            var msg = new ChatMessage
            {
                Id = ChatHistory.GetNextMessageId(channel),
                FromUserId = sub.UserId,
                FromName = sub.DisplayName,
                Message = request.Message,
            };

            // Check to see if this is a private message to a specific user
            if (request.ToUserId != null)
            {
                // Mark the message as private so it can be displayed differently in Chat
                msg.Private = true;
                // Send the message to the specific user Id
                ServerEvents.NotifyUserId(request.ToUserId, request.Selector, msg);

                // Also provide UI feedback to the user sending the private message so they
                // can see what was sent. Relay it to all senders active subscriptions 
                var toSubs = ServerEvents.GetSubscriptionsByUserId(request.ToUserId);
                foreach (var toSub in toSubs)
                {
                    // Change the message format to contain who the private message was sent to
                    msg.Message = "@{0}: {1}".Fmt(toSub.DisplayName, msg.Message);
                    ServerEvents.NotifySubscription(request.From, request.Selector, msg);
                }
            }
            else
            {
                // Notify everyone in the channel for public messages
                ServerEvents.NotifyChannel(request.Channel, request.Selector, msg);
            }

            if (!msg.Private)
                ChatHistory.Log(channel, msg);

            return msg;
        }

        public object Any(GetChatHistory request)
        {
            return new GetChatHistoryResponse
            {
                Results = ChatHistory.GetRecentChatHistory(request.Channel, request.AfterId, request.Take)
            };
        }
    }

    public class Global : System.Web.HttpApplication
    {
        protected void Application_Start(object sender, EventArgs e)
        {
            new AppHost().Init();
        }
    }
}