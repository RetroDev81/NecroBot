﻿#region using directives

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.PoGoUtils;
using PoGo.NecroBot.Logic.State;
using POGOProtos.Data;
using POGOProtos.Enums;
using POGOProtos.Inventory.Item;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Service.TelegramCommand;

#endregion

namespace PoGo.NecroBot.Logic.Service
{
    public class TelegramService
    {
        private DateTime _lastLoginTime;
        private readonly TelegramBotClient _bot;
        private bool _loggedIn;
        private readonly ISession _session;
        private List<ICommand> handlers;
        public TelegramService(string apiKey, ISession session)
        {
            try
            {
                var type = typeof(ICommand);
                var types = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(s => s.GetTypes())
                    .Where(p => type.IsAssignableFrom(p) && p != type);

                handlers = new List<ICommand>();
                foreach (var item in types)
                {
                    handlers.Add((ICommand)Activator.CreateInstance(item));
                }

                _bot = new TelegramBotClient(apiKey);
                _session = session;

                var me = _bot.GetMeAsync().Result;

                _bot.OnMessage += OnTelegramMessageReceived;
                _bot.StartReceiving();

                _session.EventDispatcher.Send(new NoticeEvent { Message = "Using TelegramAPI with " + me.Username });
            }
            catch (Exception)
            {
                _session.EventDispatcher.Send(new ErrorEvent { Message = "Unkown Telegram Error occured. " });
            }
        }

        private async void OnTelegramMessageReceived(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;
            if (message == null || message.Type != MessageType.TextMessage)
                return;

            var answerTextmessage = "";

            if (_session.Profile == null || _session.Inventory == null)
            {
                return;
            }

            var messagetext = message.Text.ToLower().Split(' ');

            if (!_loggedIn && messagetext[0].ToLower().Contains("/login"))
            {
                if (messagetext.Length == 2)
                {
                    if (messagetext[1].ToLower().Contains(_session.LogicSettings.TelegramPassword))
                    {
                        _loggedIn = true;
                        _lastLoginTime = DateTime.Now;
                        answerTextmessage += _session.Translation.GetTranslation(TranslationString.LoggedInTelegram);
                        await SendMessage(message.Chat.Id, answerTextmessage);
                        return;
                    }
                    answerTextmessage += _session.Translation.GetTranslation(TranslationString.LoginFailedTelegram);
                    await SendMessage(message.Chat.Id, answerTextmessage);
                    return;
                }
                answerTextmessage += _session.Translation.GetTranslation(TranslationString.NotLoggedInTelegram);
                await SendMessage(message.Chat.Id, answerTextmessage);
                return;
            }
            if (_loggedIn)
            {
                if (_lastLoginTime.AddMinutes(5).Ticks < DateTime.Now.Ticks)
                {
                    _loggedIn = false;
                    answerTextmessage += _session.Translation.GetTranslation(TranslationString.NotLoggedInTelegram);
                    await SendMessage(message.Chat.Id, answerTextmessage);
                    return;
                }
                var remainingMins = _lastLoginTime.AddMinutes(5).Subtract(DateTime.Now).Minutes;
                var remainingSecs = _lastLoginTime.AddMinutes(5).Subtract(DateTime.Now).Seconds;
                answerTextmessage += _session.Translation.GetTranslation(TranslationString.LoginRemainingTime,
                    remainingMins, remainingSecs);
                await SendMessage(message.Chat.Id, answerTextmessage);
                return;
            }

            if(messagetext[0].ToLower() == "/loc") { 
                    SendLocation(message.Chat.Id, _session.Client.CurrentLatitude, _session.Client.CurrentLongitude);
                return;
            }
            bool handled = false;
            Action<string> OnMessageCallback = async (string msg) =>
            {
                try
                {
                    await SendMessage(message.Chat.Id, msg);
                }
                catch (Exception ex)
                {
                }
            };
            foreach (var item in this.handlers)
            {
                handled = await item.OnCommand(_session, messagetext[0].ToLower(), OnMessageCallback);
                if (handled) break;
            }
            
            if(!handled)
            {
                HelpCommand helpCMD = new HelpCommand();
                await helpCMD.OnCommand(_session, helpCMD.Command, OnMessageCallback);
            }
        }

        private async void SendLocation(long chatId, double currentLatitude, double currentLongitude)
        {
            await _bot.SendLocationAsync(chatId, (float)currentLatitude, (float)currentLongitude);
        }

        private async Task SendMessage(long chatId, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            await _bot.SendTextMessageAsync(chatId, message, replyMarkup: new ReplyKeyboardHide());
        }
    }
}