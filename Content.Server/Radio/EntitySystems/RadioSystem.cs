using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server._EinsteinEngine.Language; // Corvax-Languages
using Content.Server.Power.Components;
using Content.Server.Radio.Components;
using Content.Server.VoiceMask;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Content.Shared._EinsteinEngine.Language; // Corvax-Languages
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;

namespace Content.Server.Radio.EntitySystems;

/// <summary>
///     This system handles intrinsic radios and the general process of converting radio messages into chat messages.
/// </summary>
public sealed class RadioSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly IReplayRecordingManager _replay = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly LanguageSystem _language = default!; // Corvax-Languages

    // set used to prevent radio feedback loops.
    private readonly HashSet<string> _messages = new();

    private EntityQuery<TelecomExemptComponent> _exemptQuery;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IntrinsicRadioReceiverComponent, RadioReceiveEvent>(OnIntrinsicReceive);
        SubscribeLocalEvent<IntrinsicRadioTransmitterComponent, EntitySpokeEvent>(OnIntrinsicSpeak);

        _exemptQuery = GetEntityQuery<TelecomExemptComponent>();
    }

    private void OnIntrinsicSpeak(EntityUid uid, IntrinsicRadioTransmitterComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null && component.Channels.Contains(args.Channel.ID))
        {
            SendRadioMessage(uid, args.Message, args.Channel, uid, args.Language); // Corvax-Languages
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    private void OnIntrinsicReceive(EntityUid uid, IntrinsicRadioReceiverComponent component, ref RadioReceiveEvent args)
    {
        if (TryComp(uid, out ActorComponent? actor))
        // Corvax-Languages-Start
        {
            var listener = component.Owner;
            var msg = args.OriginalChatMsg;
            if (listener != null && !_language.CanUnderstand(listener, args.Language.ID))
                msg = args.LanguageObfuscatedChatMsg;

            _netMan.ServerSendMessage(new MsgChatMessage { Message = msg}, actor.PlayerSession.Channel);
        }
        // Corvax-Languages-End
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    public void SendRadioMessage(EntityUid messageSource, string message, ProtoId<RadioChannelPrototype> channel, EntityUid radioSource, LanguagePrototype? language = null, bool escapeMarkup = true) // Corvax-Languages
    {
        SendRadioMessage(messageSource, message, _prototype.Index(channel), radioSource, escapeMarkup: escapeMarkup, language: language); // Corvax-Languages
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    /// <param name="messageSource">Entity that spoke the message</param>
    /// <param name="radioSource">Entity that picked up the message and will send it, e.g. headset</param>
    public void SendRadioMessage(EntityUid messageSource, string message, RadioChannelPrototype channel, EntityUid radioSource, LanguagePrototype? language = null, bool escapeMarkup = true) // Corvax-Languages
    // Corvax-Languages-Start
    {
        if (language == null)
            language = _language.GetLanguage(messageSource);

        if (!language.SpeechOverride.AllowRadio)
            return;
    // Corvax-Languages-End
        // TODO if radios ever garble / modify messages, feedback-prevention needs to be handled better than this.
        if (!_messages.Add(message))
            return;

        var name = TryComp(messageSource, out VoiceMaskComponent? mask) && mask.Enabled
            ? mask.VoiceName
            : MetaData(messageSource).EntityName;

        name = FormattedMessage.EscapeText(name);

        SpeechVerbPrototype speech;
        if (mask != null
            && mask.Enabled
            && mask.SpeechVerb != null
            && _prototype.TryIndex<SpeechVerbPrototype>(mask.SpeechVerb, out var proto))
        {
            speech = proto;
        }
        else
            speech = _chat.GetSpeechVerb(messageSource, message);

        var content = escapeMarkup
            ? FormattedMessage.EscapeText(message)
            : message;
// Corvax-Languages-Start
        var wrappedMessage = WrapRadioMessage(messageSource, channel, name, content, language);
        var msg = new ChatMessage(ChatChannel.Radio, content, wrappedMessage, NetEntity.Invalid, null);

        // ... you guess it
        var obfuscated = _language.ObfuscateSpeech(content, language);
        var obfuscatedWrapped = WrapRadioMessage(messageSource, channel, name, obfuscated, language);
        var notUdsMsg = new ChatMessage(ChatChannel.Radio, obfuscated, obfuscatedWrapped, NetEntity.Invalid, null);

        var ev = new RadioReceiveEvent(messageSource, channel, msg, notUdsMsg, language, radioSource);
// Corvax-Languages-End
        var sendAttemptEv = new RadioSendAttemptEvent(channel, radioSource);
        RaiseLocalEvent(ref sendAttemptEv);
        RaiseLocalEvent(radioSource, ref sendAttemptEv);
        var canSend = !sendAttemptEv.Cancelled;

        var sourceMapId = Transform(radioSource).MapID;
        var hasActiveServer = HasActiveServer(sourceMapId, channel.ID);
        var sourceServerExempt = _exemptQuery.HasComp(radioSource);

        var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();
        while (canSend && radioQuery.MoveNext(out var receiver, out var radio, out var transform))
        {
            if (!radio.ReceiveAllChannels)
            {
                if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                             !intercom.SupportedChannels.Contains(channel.ID)))
                    continue;
            }

            if (!channel.LongRange && transform.MapID != sourceMapId && !radio.GlobalReceive)
                continue;

            // don't need telecom server for long range channels or handheld radios and intercoms
            var needServer = !channel.LongRange && !sourceServerExempt;
            if (needServer && !hasActiveServer)
                continue;

            // check if message can be sent to specific receiver
            var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
            RaiseLocalEvent(ref attemptEv);
            RaiseLocalEvent(receiver, ref attemptEv);
            if (attemptEv.Cancelled)
                continue;

            // send the message
            RaiseLocalEvent(receiver, ref ev);
        }

        if (name != Name(messageSource))
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} as {name} on {channel.LocalizedName}: {message}");
        else
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} on {channel.LocalizedName}: {message}");

        _replay.RecordServerMessage(msg); // Corvax-Languages
        _messages.Remove(message);
    }
// Corvax-Languages-Start
    private string WrapRadioMessage(EntityUid source, RadioChannelPrototype channel, string name, string message, LanguagePrototype language)
    {
        // TODO: code duplication with ChatSystem.WrapMessage
        var speech = _chat.GetSpeechVerb(source, message);
        var languageColor = channel.Color;
        if (language.SpeechOverride.Color is { } colorOverride)
            languageColor = Color.InterpolateBetween(languageColor, colorOverride, colorOverride.A);

        return Loc.GetString(speech.Bold ? "chat-radio-message-wrap-bold" : "chat-radio-message-wrap",
            ("color", channel.Color),
            ("languageColor", languageColor),
            ("fontType", language.SpeechOverride.FontId ?? speech.FontId),
            ("fontSize", language.SpeechOverride.FontSize ?? speech.FontSize),
            ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
            ("channel", $"\\[{channel.LocalizedName}\\]"),
            ("name", name),
            ("message", message));
    }
// Corvax-Languages-End
    /// <inheritdoc cref="TelecomServerComponent"/>
    private bool HasActiveServer(MapId mapId, string channelId)
    {
        var servers = EntityQuery<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        foreach (var (_, keys, power, transform) in servers)
        {
            if (transform.MapID == mapId &&
                power.Powered &&
                keys.Channels.Contains(channelId))
            {
                return true;
            }
        }
        return false;
    }
}
