Chat (beta)
===========

Chat showcases ServiceStack's new support for [Server Sent Events](http://www.html5rocks.com/en/tutorials/eventsource/basics/) with a cursory Chat app packed with a number of features including:

  - Anonymous or Authenticated access with Twitter, Facebook or GitHub OAuth
  - Joining any arbitrary user-defined channel
  - Private messaging
  - Command history
  - Autocomplete of user names
  - Highlighting of mentions
  - Grouping messages by user
  - Active list of users, kept live with:
    - Periodic Heartbeats
    - Automatic unregistration on page unload
  - Remote Control
    - Send a global announcement to all users
    - Toggle on/off channel controls
    - Change the CSS style of any element
    - Change the HTML document's title
    - Redirect users to any url
    - Play a youtube video
    - Display an image url
    - Raise DOM events

### Feature Preview

![Chat Overview](https://raw.githubusercontent.com/ServiceStack/Assets/master/img/apps/Chat/chat-overview.gif)

### Super lean front and back

All this fits in a tiny footprint using just vanilla jQuery weighing just:

  - [1 default.cshtml page](https://github.com/ServiceStackApps/Chat/blob/master/src/Chat/default.cshtml), with just **60 lines of HTML markup** and **165 lines of JavaScript**
  - [2 ServiceStack Services](https://github.com/ServiceStackApps/Chat/blob/master/src/Chat/Global.asax.cs)

On the back-end Chat utilizes a variety of different Web Framework features spanning Dyanmic Razor Views, Web Services, JSON Serialization, Server Push Events as well as Twitter, Facebook and GitHub OAuth integration, all in a single page ASP.NET ServiceStack WebApp running on **9 .NET dll** dependencies.

## Remote control

The most interesting feature in Chat is its showcase of ServiceStack's JS client bindings which provide a number of different ways to interact and modify a live webapp by either:

  - Invoking Global Event Handlers
  - Modifying CSS via jQuery
  - Sending messages to Receivers
  - Raising jQuery Events

All options above are designed to integrate with existing functionality by being able to invoke predefined handlers, object instances as well as modify jQuery CSS and raising DOM events.

Clients can apply any of the above methods to all users, a specified user or just itself.

## Server Sent Events

[Server Sent Events](http://www.html5rocks.com/en/tutorials/eventsource/basics/) (SSE) is an elegant [web technology](http://dev.w3.org/html5/eventsource/) for efficiently receiving push notifications from any HTTP Server. It can be thought of as a mix between long polling and one-way WebSockets and contains many benefits over each:

  - **Simple** - Server Sent Events is just a single long-lived HTTP Request that any HTTP Server can support
  - **Efficient** - Each client uses a single TCP connection and each message avoids the overhead of HTTP Connections and Headers that's [often faster than Web Sockets](http://matthiasnehlsen.com/blog/2013/05/01/server-sent-events-vs-websockets/).
  - **Resilient** - Browsers automatically detect when a connection is broken and automatically reconnects
  - **Interoperable** - As it's just plain-old HTTP, it's introspectable with your favorite HTTP Tools and even works through HTTP proxies (with buffering and checked-encoding turned off).
  - **Well Supported** - As a Web Standard it's supported in all major browsers except for IE which [can be enabled with polyfills](http://html5doctor.com/server-sent-events/#yaffle).

### Registering

List most other [modular functionality](https://github.com/ServiceStack/ServiceStack/wiki/Plugins) in ServiceStack, Server Sent Events is encapsulated in a single Plugin that can be registered in your AppHost with:

```csharp
Plugins.Add(new ServerEventsFeature());
```

The registration above is all that's needed for most use-cases which just uses the defaults below:

```csharp
class ServerEventsFeature
{
    StreamPath = "/event-stream";            // The entry-point for Server Sent Events
    HeartbeatPath = "/event-heartbeat";      // Where to send heartbeat pulses
    UnRegisterPath = "/event-unregister";    // Where to unregister your subscription
    SubscribersPath = "/event-subscribers";  // Where to view public info of channel subscribers 

    Timeout = TimeSpan.FromSeconds(30);           // How long to wait for a heartbeat before unsubscribing
    HeartbeatInterval = TimeSpan.FromSeconds(10); // Client Interval for sending heartbeat messages

    NotifyChannelOfSubscriptions = true;          // Send notifications when subscribers join/leave
}
```

> The paths allow you to customize the routes for the built-in Server Events API's, whilst setting either path to `null` disables that feature. 

There are also a number of hooks available providing entry points where custom logic can be added to modify or enhance existing behavior:

```csharp
class ServerEventsFeature
{
    Action<IEventSubscription, Dictionary<string, string>> OnConnect; // Filter for OnConnect messages

    Action<IEventSubscription, IRequest> OnCreated;  // Fired when an Subscription is created
    Action<IEventSubscription> OnSubscribe;          // Fired when subscription is registered 
    Action<IEventSubscription> OnUnsubscribe;        // Fired when subscription is unregistered
}
```

### Sending Server Events

The way your Services send notifications is via the `IServerEvents` API which currently only has an in-memory `MemoryServerEvents` implementation which keeps a record of all subscriptions and connections in memory:

> Based on feedback we'll also consider adding a distributed Redis implementation so events can be sent across load-balanced app servers.

```csharp
public interface IServerEvents
{
    // External API's
    void NotifyAll(string selector, object message);

    void NotifyChannel(string channel, string selector, object message);

    void NotifySubscription(string subscriptionId, string selector, object message, string channel = null);

    void NotifyUserId(string userId, string selector, object message, string channel = null);

    void NotifyUserName(string userName, string selector, object message, string channel = null);

    void NotifySession(string sspid, string selector, object message, string channel = null);

    IEventSubscription GetSubscription(string id);

    List<IEventSubscription> GetSubscriptionsByUserId(string userId);

    // Admin API's
    void Register(IEventSubscription subscription);

    void UnRegister(IEventSubscription subscription);

    // Client API's
    List<Dictionary<string, string>> GetSubscriptions(string channel = null);

    void Pulse(string id);
}
```

The API's your Services predominantly deal with are the **External API's** which allow sending of messages at different levels of granularity. As Server Events have deep integration with ServiceStack's [Sessions](https://github.com/ServiceStack/ServiceStack/wiki/Sessions) and [Authentication Providers](https://github.com/ServiceStack/ServiceStack/wiki/Authentication-and-authorization) you're also able to notify specific users by either:

```csharp
`NotifyUserId()   // UserAuthId
`NotifyUserName() // UserName
`NotifySession()  // Permanent Session Id (ss-pid)
```

Whilst these all provide different ways to send a message to a single authenticated user, any user can be connected to multiple subscriptions at any one time (e.g. by having multiple tabs open). Each one of these subscriptions is uniquely identified by a `subscriptionId` which you can send a message with using: 

  - `NotifySubscription()`   - Unique Subscription Id

There are also API's to retrieve a users single event subscription as well as all subscriptions for a user:

```csharp
IEventSubscription GetSubscription(string id);

List<IEventSubscription> GetSubscriptionsByUserId(string userId);
```

### Event Subscription

An Event Subscription allows you to inspect different metadata contained on each subscription as well as being able to `Publish()` messages directly to it, manually send a Heartbeat `Pulse()` (to keep the connection active) as well as `Unsubscribe()` to revoke the subscription and terminate the HTTP Connection.

```csharp
public interface IEventSubscription : IMeta, IDisposable
{
    DateTime CreatedAt { get; set; }
    DateTime LastPulseAt { get; set; }

    string Channel { get; }
    string UserId { get; }
    string UserName { get; }
    string DisplayName { get; }
    string SessionId { get; }
    string SubscriptionId { get; }
    bool IsAuthenticated { get; set; }

    Action<IEventSubscription> OnUnsubscribe { get; set; }
    void Unsubscribe();

    void Publish(string selector, object message);

    void Pulse();
}
```

The `IServerEvents` API also offers an API to UnRegister a subscription with:


```csharp
void UnRegister(IEventSubscription subscription);
```

## Channels

Standard Publish / Subscribe patterns include the concept of a **Channel** upon which to subscribe and publish messages to. The channel in Server Events can be any arbitrary string which is declared on the fly when it's first used. 

> As Request DTO names are unique in ServiceStack they also make good channel names which benefit from providing a typed API for free, e.g: `typeof(Request).Name`.

The API to send a message to a specific channel is:

```csharp
void NotifyChannel(string channel, string selector, object message);
```

Which just includes the name of the `channel`, the `selector` you wish the message applies to and the `message` to send which can be any JSON serializable object.

Along with being able to send a message to everyone on a channel, each API also offers an optional `channel` filter which when supplied will limit messages only to that channel:

```csharp
void NotifyUserId(string userId, string selector, object message, string channel = null);
```

## Client Bindings - ss-utils.js

Like ServiceStack's other JavaScript interop libraries, the client bindings for ServiceStack's Server Events is in `ServiceStack.dll` and is available on any page with:

```html
<script src="/js/ss-utils.js"></script>
```

To configure Server Sent Events on the client create a [native EventSource object](http://www.html5rocks.com/en/tutorials/eventsource/basics/) with:

```javascript
var source = new EventSource('/event-stream?channel=channel&t=' + new Date().getTime());
```

> The default url `/event-stream` can be modified with `ServerEventsFeature.StreamPath`

As this is the native `EventSource` object, you can interact with it directly, e.g. you can add custom error handlers with:

```javascript
source.addEventListener('error', function (e) { 
        console.log("ERROR!", e); 
    }, false);
```

The ServiceStack binding itself is just a thin jQuery plugin that extends `EventSource`, e.g:

```javascript
$(source).handleServerEvents({
    handlers: {
        onConnect: function (subscription) {
            activeSub = subscription;
            console.log("This is you joining, welcome " + u.displayName + ", img: " + u.profileUrl);
        },
        onJoin: function (user) {
            console.log("Welcome, " + user.displayName);
        },
        onLeave: function (user) {
            console.log(user.displayName + " #" + user.userId + ": has left the building");
        },
        //... Register custom handlers
    },
    receivers { 
        //... Register any receivers
    },
    success: function (selector, msg, json) { // fired after every message
        console.log(selector, msg, json);
    },
});
```

ServiceStack Server Events has 3 built-in events sent during a subscriptions life-cycle: 

 - **onConnect** - sent when successfully connected, includes the subscriptions private `subscriptionId` as well as heartbeat and unregister urls that's used to automatically setup periodic heartbeats.
 - **onJoin** - sent when a new user joins the channel.
 - **onLeave** - sent when a user leaves the channel.

> The onJoin/onLeave events can be turned off with `ServerEventsFeature.NotifyChannelOfSubscriptions=false`.

## Selectors

A selector is a string that identifies what should handle the message, it's used by the client to route the message to different handlers. The client bindings in [/js/ss-utils.js](https://github.com/ServiceStack/EmailContacts/#servicestack-javascript-utils---jsss-utilsjs) supports 4 different handlers out of the box:

### Global Event Handlers

To recap [Declarative Events](https://github.com/ServiceStack/EmailContacts/#declarative-events) allow you to define global handlers on a html page which can easily be applied on any element by decorating it with `data-{event}='{handler}'` attribute, eliminating the need to do manual bookkeeping of DOM events. 

The example below first invokes the `paintGreen` handler when the button is clicked and fires the `paintRed` handler when the button loses focus:

```javascript
$(document).bindHandlers({
    paintGreen: function(){
        $(this).css("background","green");
    }, 
    paintRed: function(){
        $(this).css("background","red");
    }, 
});
```

```html
<button id="btnPaint" data-click="paintGreen" data-focusout="paintRed">Paint Town</button>
```

The selector to invoke a global event handler is:

    cmd.{handler}

Where `{handler}` is the name of the handler you want to invoke, e.g `cmd.paintGreen`. When invoked from a server event the message (deserialized from JSON) is the first argument, the Server Sent DOM Event is the 2nd argument and `this` by default is assigned to `document.body`.

```javascript
function paintGreen(msg /* JSON object msg */, e /*SSE DOM Event*/){
    this // HTML Element or document.body
    $(this).css("background","green");
},
```

### Postfix jQuery selector 

All server event handler options also support a postfix jQuery selector for specifying what each handler should be bound to with a `$` followed by the jQuery selector, e.g:

    cmd.{handler}${jQuerySelector}

An concrete example for calling the above API would be:

    cmd.paintGreen$#btnPaint

Which will bind `this` to the `#btnSubmit` HTML Element, retaining the same behavior as if it were called with `data-click="paintGreen".

> Note: Spaces in jQuery selectors need to be encoded with `%20`

### Modifying CSS via jQuery

As it's a popular use-case Server Events also has native support for modifying CSS properties on any jQuery with:

    css.{propertyName}${jQuerySelector} {propertyValue}

Where the message is the property value, which will roughly translate to:

    $({jQuerySelector}).css({propertyName}, {propertyValue})

When no jQuery selector is specified it falls back to `document.body` by default.

    /css.background #eceff1

Some other examples include:

    /css.background$#top #673ab7     // $('#top').css('background','#673ab7')
    /css.font$li bold 12px verdana   // $('li').css('font','bold 12px verdana')
    /css.visibility$a,img hidden     // $('a,img').css('visibility','#673ab7')
    /css.visibility$a%20img hidden   // $('a img').css('visibility','hidden')

### jQuery Events

A popular approach in building loosely-coupled applications is to have components interact with each other by raising events. It's similar to channels in Pub/Sub where interested parties can receive and process custom events on components they're listening on. jQuery supports this model by simulating DOM events that can be raised with [$.trigger()](http://api.jquery.com/trigger/). 

You can subscribe to custom events in the same way as normal DOM events, e.g:

```javascript
$(document).on('customEvent', function(event, arg, msgEvent){
    var target = event.target;
});
```

The selector to trigger this event is:

    trigger.customEvent arg
    trigger.customEvent$#btnPaint arg

Where if no jQuery selector is specified it defaults to `document`. These selectors are equivalent to:

```javascript
$(document).trigger('customEvent', 'arg')
$("#btnPaint").trigger('customEvent', 'arg')
```

### Receivers

In programming languages based on message-passing like Smalltalk and Objective-C invoking a method is done by sending a message to a receiver. This is conceptually equivalent to invoking a method on an instance in C# where both these statements are roughly equivalent:

```objc
// Objective-C
[receiver method:argument]
```

```csharp
// C#
receiver.method(argument)
```

Support for receivers is available in the following format:

    {receiver}.{target} {msg}

### Registering Receivers

Registering a receiver can be either be done by adding it to the `$.ss.eventReceivers` map with the name you want the object instance and the name you want it to be exported as. E.g. The `window` and `document` global objects can be setup to receive messages with:

```javascript
$.ss.eventReceivers = { 
    "window": window, 
    "document": document 
};
```

Once registered you can set any property or call any method on a receiver with:

    document.title New Window Title
    window.location http://google.com

Where if `{target}` was a function it will be invoked with the message, otherwise its property will be set.
By default when no `{jQuerySelector}` is defined, `this` is bound to the **receiver** instance.

The alternative way to register a receiver is at registration with:

```javascript
$(source).handleServerEvents({
    ...
    receivers: {
        tv: {
            watch: function (id) {
                if (id.indexOf('youtu.be') >= 0) {
                    var v = $.ss.splitOnLast(id, '/')[1];
                    $("#tv").html(templates.youtube.replace("{id}", v)).show();
                } else {
                    $("#tv").html(templates.generic.replace("{id}", id)).show();
                }
            },
            off: function () {
                $("#tv").hide().html("");
            }
        }
    }
});
```

This registers a custom `tv` receiver that can now be called with:

    tv.watch http://youtu.be/518XP8prwZo
    tv.watch https://servicestack.net/img/logo-220.png
    tv.off

### Un Registering a Receiver

As receivers are maintained in a simple map, they can be disabled at anytime with:

```javascript
$.ss.eventReceivers["window"] = null; //or delete $.ss.eventReceivers["window"]
```

and re-enabled with:

```javascript
$.ss.eventReceivers["window"] = window;
```

## Chat Features

The implementation of Chat is a great way to explore different Server Event features which make it easy to develop highly interactive and responsive web apps with very little effort. 

### Active Subscribers

One feature common to chat clients is to get details of all the active subscribers in a channel which we can get from the built-in `/event-subscribers` route, e.g:

```javascript
$.getJSON("/event-subscribers?channel=@channel", function (users) {
    $.map(users, function(user) {
        usersMap[user.userId] = user;
        refCounter[user.userId] = (refCounter[user.userId] || 0) + 1;
    });
    var html = $.map(usersMap, function(user) { return createUser(user); }).join('');
    $("#users").html(html);
});
```

As a single user can have multiple subscriptions (e.g. multiple tabs open) users are merged into a single `usersMap` so each user is only listed once in the users list and a `refCounter` is maintained with the number of subscriptions each user has, so we know can tell when the user has no more active subscriptions and can remove them from the list.

### Chat box

Chat's text box provides a free-text entry input to try out different Server Event features where each text message is posted to a ServiceStack Service which uses the `IServerEvents` API to send notifications the channels subscribers. When a server event is received on the client, the ss-utils.js client bindings routes the message to the appropriate handler. As all messages go through this same process, the moment the log entry appears in your chat window is also when it appears for everyone else (i.e instant when running localhost).

Normal chat messages (i.e. that don't specify a selector) uses the default `cmd.chat` selector which is sent to the `chat` handler that just echoes the entry into the chat log with:

```javascript
chat: function (m, e) {
    addEntry({ id: m.id, userId: m.fromUserId, userName: m.fromName, msg: m.message, 
               cls: m.private ? ' private' : '' });
}

```

### Specifying a selector

You can specify to use an alternative selector by prefixing the message with a `/`, e.g: 

    /cmd.announce This is your captain speaking ...

When a selector is specified in Chat it routes the message to `/channels/{Channel}/raw` route which passes the text through as-is. Normal Chat entries are instead posted to the `/channels/{Channel}/chat` service, adding adds additional metadata to the chat message with the user id and name of the sender so it can also be displayed in the chat log. The Javascript code that does the routing is simply: 

```javascript
if (msg[0] == "/") {
    parts = $.ss.splitOnFirst(msg, " ");
    $.post("/channels/@channel/raw", 
        { from: activeSub.id, toUserId: to, message: parts[1], selector: parts[0].substring(1) });
} else {
    $.post("/channels/@channel/chat", 
        { from: activeSub.id, toUserId: to, message: msg, selector: "cmd.chat" });
}
```

### Sending a message to a specific user

Another special syntax supported in Chat is the ability to send messages to other users by prefixing it with `@` followed by the username, e.g:

    @mythz this is a private message
    @mythz /tv.watch http://youtu.be/518XP8prwZo

There's also a special `@me` alias to send a message to yourself, e.g:

    @me /tv.watch http://youtu.be/518XP8prwZo

## Server Event Services

By default ServiceStack doesn't expose any Services that can send notifications to other users by default.  It's left up to your application what functionality and level of granularity you wish to enable for your Application. You send notifications in your services `IServerEvents`

Below is the annotated implementation for both Web Services used by Chat. The `PostRawToChannel` is a simple implementation that just relays the message sent to all users in the channel or just a specific user if `ToUserId` parameter is specified.

The `PostChatToChannel` Service is used for sending Chat messages which sends a wrapped `ChatMessage` DTO instead that holds additional metadata about the message that the Chat UI requires:

```csharp
[Route("/channels/{Channel}/chat")]
public class PostChatToChannel : IReturn<ChatMessage>
{
    public string From { get; set; }
    public string ToUserId { get; set; }
    public string Channel { get; set; }
    public string Message { get; set; }
    public string Selector { get; set; }
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

public class ServerEventsServices : Service
{
    private static long msgId;

    public IServerEvents ServerEvents { get; set; }

    public void Any(PostRawToChannel request)
    {
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

        // Create a DTO ChatMessage to hold all required info about this message
        var msg = new ChatMessage
        {
            Id = Interlocked.Increment(ref msgId),
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

        return msg;
    }
}
```



