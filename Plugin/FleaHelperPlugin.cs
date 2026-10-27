#nullable disable
using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Comfort.Common;
using EFT.Communications;
using FleaHelper.Patches;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;



namespace FleaHelper
{
    [BepInPlugin("zzzNIKOzzz.fleahelper", "FleaHelper", PluginVersion)]
    internal class FleaHelperPlugin : BasePlugin
    {
        public const string PluginVersion = "1.0.0";
        internal static new ManualLogSource Log;
        public static ConfigEntry<KeyCode> Hotkey { get; set; }
        public static ConfigEntry<bool> ShowListingPrice { get; set; }
        public static ConfigEntry<bool> BypassLimit { get; set; }
        public static ConfigEntry<bool> SkipMode { get; set; }
        public static ConfigEntry<EPostingCurrency> PostingCurrency { get; set; }
        public static ConfigEntry<KeyCode> ChangeCurrencyKey { get; set; }
        private static readonly EPostingCurrency[] _currencyValues =
            (EPostingCurrency[])Enum.GetValues(typeof(EPostingCurrency));

        private static readonly Dictionary<EPostingCurrency, string> CurrencyIds = new()
    {
        { EPostingCurrency.RUB, "5449016a4bdc2d6f028b456f" },  
        { EPostingCurrency.USD, "5696686a4bdc2d6f028b456a" },  
        { EPostingCurrency.EUR, "569668774bdc2d6f028b4568" }   
    };

        private static Dictionary<string, string> _localeCache = null;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz");
            Log.LogInfo("_____FleaHelperPlugin от zzzNIKOzzz загружается____");

            Hotkey = Config.Bind("FleaHelper", "Быстрая клавиша", KeyCode.LeftControl,
                "Клавиша, которая отвечает за быструю продажу");
            ShowListingPrice = Config.Bind("FleaHelper", "Показать цену лота", false,
                "Показывать цену лота в всплывающей подсказке");
            BypassLimit = Config.Bind("FleaHelper", "Лимит продажи", false,
                "Игнорировать лимиты выставления лотов на барахолке");
            SkipMode = Config.Bind("FleaHelper", "Skip Mode", true,
                "Отключить ванильный мигающий эффект при ожидании ответа сервера");
            PostingCurrency = Config.Bind("FleaHelper", "Валюта продажи", EPostingCurrency.RUB,
                "Валюта выставления лота рубли, доллары, евро");
            ChangeCurrencyKey = Config.Bind("FleaHelper", "Бытрое изменение валюты", KeyCode.None,
                "Клавиша для быстрого переключения валюты продажи");

            var harmony = new Harmony("zzzNIKOzzz.fleahelper");
            harmony.PatchAll();
            AddComponent<FleaHelperBehaviour>();
            Log.LogInfo("zzzNIKOzzz: Я сейчас ВСЕ предметы буду продавать!!!");
            Log.LogInfo("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz");
        }

        internal static void ChangeCurrency()
        {
            var currentIndex = Array.IndexOf(_currencyValues, PostingCurrency.Value);
            PostingCurrency.Value = _currencyValues[(currentIndex + 1) % _currencyValues.Length];
            ShowNotification("Currency set to " + PostingCurrency.Value);
        }

        private static bool IsItemSellableOnFlea(object item, Type itemType)
        {
            try
            {
                object template = GetPropertyValue(item, itemType, "Template");
                if (template == null) return true;

                var templateType = template.GetType();

                foreach (var name in new[] { "QuestItem", "IsQuestItem" })
                {
                    object val = GetPropertyValue(template, templateType, name)
                                 ?? GetFieldValue(template, templateType, name);
                    if (val is bool b && b)
                    {
                        Log.LogInfo($"[FleaHelper] Нельзя продать: {name}=true");
                        return false;
                    }
                }

                foreach (var name in new[] { "CanSellOnRagfair", "CanSellOnFlea", "SellableOnFlea", "SellableOnRagfair" })
                {
                    object val = GetPropertyValue(template, templateType, name)
                                 ?? GetFieldValue(template, templateType, name);
                    if (val is bool b && !b)
                    {
                        Log.LogInfo($"[FleaHelper] Нельзя продать: {name}=false");
                        return false;
                    }
                }

                foreach (var name in new[] { "NotSellable", "IsNotSellable" })
                {
                    object val = GetPropertyValue(template, templateType, name)
                                 ?? GetFieldValue(template, templateType, name);
                    if (val is bool b && b)
                    {
                        Log.LogInfo($"[FleaHelper] Нельзя продать: {name}=true");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.LogWarning("[FleaHelper] Ошибка проверки sellable: " + ex.Message);
                return true;
            }
        }

        internal static void PostItemToFlea()
        {
            try
            {         
                Type itemUiContextType = FindType("EFT.UI.ItemUiContext");
                if (itemUiContextType == null)
                {
                    ShowNotification("ItemUiContext not found");
                    return;
                }
                object itemUiContext = null;

                
                var staticInstanceProp = itemUiContextType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (staticInstanceProp != null)
                {
                    itemUiContext = staticInstanceProp.GetValue(null);
                }

                if (itemUiContext == null)
                    itemUiContext = GetSingletonInstance(itemUiContextType);

                
                if (itemUiContext == null)
                {
                    try
                    {
                        var findMethod = typeof(UnityEngine.Object).GetMethod("FindObjectOfType",
                            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null);
                        if (findMethod != null)
                            itemUiContext = findMethod.Invoke(null, new object[] { itemUiContextType });
                    }
                    catch { }
                }

                if (itemUiContext == null)
                {
                    ShowNotification("Нет контекста предмета (нет в инвентаре?)");
                    return;
                }
                
                object itemContext = GetPropertyValue(itemUiContext, itemUiContextType, "CurrentItemContext")
                                     ?? GetPropertyValue(itemUiContext, itemUiContextType, "_CurrentItemContext_k__BackingField");

                if (itemContext == null)
                {
                    ShowNotification("Предмет не выбран");
                    return;
                }
                
                var itemContextType = itemContext.GetType();
                object item = GetPropertyValue(itemContext, itemContextType, "Item")
                              ?? GetFieldValue(itemContext, itemContextType, "Item");

                if (item == null)
                {
                    ShowNotification("Нет предмета для продажи");
                    return;
                }

                var itemType = item.GetType();

                object templateIdObj = GetPropertyValue(item, itemType, "TemplateId");
                string templateId = ExtractMongoId(templateIdObj) ?? "unknown";

                string itemId = ExtractItemId(item, itemType);

                string shortName = GetShortName(item, itemType, templateId);
                Log.LogInfo("Имя предмета: " + shortName);

                
                if (!IsItemSellableOnFlea(item, itemType))
                {
                    ShowNotification($"Нельзя продать: {shortName} (квестовый или запрещён)");
                    return;
                }

                object handbook = GetPropertyValue(itemUiContext, itemUiContextType, "Handbook");
                int price = 0;

                if (handbook != null)
                {
                    var handbookType = handbook.GetType();

                    
                    if (templateIdObj != null)
                    {
                        var basePriceMethod = handbookType.GetMethod("GetBasePrice", BindingFlags.Public | BindingFlags.Instance);
                        if (basePriceMethod != null)
                        {
                            try
                            {
                                var result = basePriceMethod.Invoke(handbook, new object[] { templateIdObj });
                                if (result != null)
                                {
                                    price = Convert.ToInt32(result);
                                }
                            }
                            catch { }
                        }
                    }

                    if (price <= 0)
                    {
                        string[] priceMethodNames = { "GetFleaPriceForItem", "GetItemPrice", "GetFleaPrice",
                                                  "GetItemValue", "GetHandbookPrice", "GetAvgFleaPrice",
                                                  "GetItemMinAvgMaxFleaPriceValues" };
                        foreach (var methodName in priceMethodNames)
                        {
                            if (price > 0) break;
                            var priceMethod = handbookType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                            if (priceMethod == null) continue;

                            var parms = priceMethod.GetParameters();

                            try
                            {
                                
                                object[] methodArgs = new object[parms.Length];
                                for (int i = 0; i < parms.Length; i++)
                                {
                                    var pt = parms[i].ParameterType;
                                    if (pt == typeof(string))
                                        methodArgs[i] = templateId;
                                    else if (templateIdObj != null && pt.IsInstanceOfType(templateIdObj))
                                        methodArgs[i] = templateIdObj;
                                    else if (pt == typeof(int) || pt == typeof(long))
                                        methodArgs[i] = 0;
                                    else
                                        methodArgs[i] = null;
                                }

                                var result = priceMethod.Invoke(handbook, methodArgs);
                                if (result != null)
                                {
                                    price = Convert.ToInt32(result);
                                }
                            }
                            catch { }
                        }
                    }
                    
                    if (price <= 0)
                    {
                        price = 10000;
                    }
                }
                
                if (price <= 0)
                {
                    object template = GetPropertyValue(item, itemType, "Template");
                    if (template != null)
                    {
                        var templateType = template.GetType();
                        object creditsPriceObj = GetPropertyValue(template, templateType, "CreditsPrice")
                                                 ?? GetFieldValue(template, templateType, "CreditsPrice");
                        if (creditsPriceObj != null)
                        {
                            price = Convert.ToInt32(creditsPriceObj);
                        }
                    }
                }
                
                if (price <= 0)
                {
                    price = 10000;
                }
                
                object ragfairInstance = GetPropertyValue(itemUiContext, itemUiContextType, "_ragfair")
                                         ?? GetPropertyValue(itemUiContext, itemUiContextType, "Ragfair");

                if (ragfairInstance == null)
                {
                    Type ragfairType = FindType("EFT.UI.Ragfair.RagFair");
                    if (ragfairType != null)
                        ragfairInstance = GetSingletonInstance(ragfairType);
                }

                if (ragfairInstance == null)
                {
                    ShowNotification("Барахолка недоступна");
                    return;
                }

                var ragType = ragfairInstance.GetType();
                string currencyId = CurrencyIds.GetValueOrDefault(PostingCurrency.Value, CurrencyIds[EPostingCurrency.RUB]);
                bool success = CreateOffer(ragfairInstance, ragType, itemId, currencyId, price, shortName);

                if (success)
                {
                    ShowNotification($"Лот создан: {shortName} по цене {price}");
                }
                else
                {
                    ShowNotification($"Невозможно выставить лот {shortName} - смотри лог");
                }
            }
            catch (Exception ex)
            {
                Log.LogError("PostItemToFlea failed: " + ex.Message + "\n" + ex.StackTrace);
                ShowNotification("Ошибка: " + ex.Message);
            }
        }

        private static bool CreateOffer(object ragfair, Type ragType, string itemId, string currencyId, int price, string shortName)
        {
            try
            {
                
                var addOfferMethod = ragType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "AddOffer" || m.Name == "AddPlayerOffer");

                if (addOfferMethod == null)
                {
                    return false;
                }

                var parameters = addOfferMethod.GetParameters();

                object[] args = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;

                    if (paramType == typeof(bool))
                    {
                        
                        args[i] = false;
                    }
                    else if (paramType == typeof(string))
                    {
                        
                        args[i] = itemId;
                    }
                    else if (paramType.Name == "Il2CppStringArray" || paramType.Name == "String[]")
                    {
                        
                        args[i] = CreateIl2CppStringArray(itemId, paramType);
                    }
                    else if (paramType.IsArray && paramType.Name.Contains("Il2Cpp"))
                    {
                        
                        args[i] = CreateRequirementsArray(paramType, currencyId, price);
                    }
                    else if (paramType.Name.Contains("Il2CppReferenceArray"))
                    {
                        args[i] = CreateRequirementsArray(paramType, currencyId, price);
                    }
                    else if (paramType.Name == "Action" || paramType.Name.Contains("Action"))
                    {
                        
                        args[i] = CreateCallback(paramType);
                    }
                    else
                    {
                        try
                        {
                            args[i] = paramType.IsValueType ? Activator.CreateInstance(paramType) : null;
                        }
                        catch
                        {
                            args[i] = null;
                        }
                    }
                }

                addOfferMethod.Invoke(ragfair, args);
                return true;
            }
            catch (Exception ex)
            {
                Log.LogError("Ошибка создания предложения: " + ex.Message + "\n" + ex.StackTrace);
                return false;
            }
        }
        
        private static object CreateIl2CppStringArray(string itemId, Type arrayType)
        {
            
            var ctor = arrayType.GetConstructor(new[] { typeof(string[]) });
            if (ctor != null)
            {
                return ctor.Invoke(new object[] { new[] { itemId } });
            }

            ctor = arrayType.GetConstructor(new[] { typeof(int) });
            if (ctor != null)
            {
                var arr = ctor.Invoke(new object[] { 1 });
                var indexer = arrayType.GetProperty("Item");
                if (indexer != null)
                {
                    indexer.SetValue(arr, itemId, new object[] { 0 });
                }
                return arr;
            }

            try
            {
                var elementType = arrayType.GetElementType();
                if (elementType != null)
                {
                    var il2cppType = Il2CppType.From(elementType);
                    var arr = Il2CppSystem.Array.CreateInstance(il2cppType, 1);
                    
                    arr.SetValue((Il2CppSystem.Object)(Il2CppSystem.String)itemId, 0);
                    return arr;
                }
            }
            catch { }

            return null;
        }
        
        private static object CreateRequirementsArray(Type arrayType, string currencyId, int price)
        {
            try
            {
                
                
                
                Type elementType = arrayType.GetElementType();
                if (elementType == null && arrayType.IsGenericType)
                    elementType = arrayType.GetGenericArguments().FirstOrDefault();

                if (elementType == null)
                {
                    return null;
                }

                
                object requirement = null;
                try
                {
                    requirement = Activator.CreateInstance(elementType);
                }
                catch
                {
                    return null;
                }

                if (requirement == null)
                {
                    return null;
                }
                
                
                
                SetFieldOrProp(requirement, elementType, "_tpl", currencyId);
                SetFieldOrProp(requirement, elementType, "count", (double)price);

                
                try
                {
                    var ctor = arrayType.GetConstructor(new[] { typeof(int) });
                    if (ctor != null)
                    {
                        var arr = ctor.Invoke(new object[] { 1 });
                        var indexer = arrayType.GetProperty("Item");
                        if (indexer != null)
                        {
                            indexer.SetValue(arr, (Il2CppSystem.Object)requirement, new object[] { 0 });
                            return arr;
                        }
                    }
                }
                catch { }

                
                try
                {
                    var arrCtor = arrayType.GetConstructor(new[] { elementType.MakeArrayType() });
                    if (arrCtor != null)
                    {
                        var arr = arrCtor.Invoke(new object[] { new[] { requirement } });
                        return arr;
                    }
                }
                catch { }

                
                try
                {
                    var il2cppElementType = Il2CppType.From(elementType);
                    var arr = Il2CppSystem.Array.CreateInstance(il2cppElementType, 1);
                    arr.SetValue((Il2CppSystem.Object)requirement, 0);
                    return arr;
                }
                catch { }

                return null;
            }
            catch (Exception ex)
            {
                Log.LogError("Ошибка создания масива требований: " + ex.Message + "\n" + ex.StackTrace);
                return null;
            }
        }
        
        
        
        
        private static object CreateCallback(Type callbackType)
        {
            
            
            Action callback = () =>
            {
            };
            
            try
            {
                var convertMethod = typeof(DelegateSupport).GetMethod("ConvertDelegate", BindingFlags.Public | BindingFlags.Static);
                if (convertMethod != null)
                {
                    var genericConvert = convertMethod.MakeGenericMethod(callbackType);
                    return genericConvert.Invoke(null, new object[] { callback });
                }
            }
            catch { }

            return null;
        }
        
        
        
        
        
        
        
        
        
        
        
        private static string ExtractMongoId(object mongoIdObj)
        {
            if (mongoIdObj == null) return null;

            
            string idStr = mongoIdObj.ToString();
            if (idStr != null && idStr.Length == 24 && IsHexString(idStr))
                return idStr;

            
            var type = mongoIdObj.GetType();
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                try
                {
                    var val = prop.GetValue(mongoIdObj);
                    if (val != null)
                    {
                        string strVal = val.ToString();
                        if (strVal.Length == 24 && IsHexString(strVal))
                        {
                            return strVal;
                        }
                    }
                }
                catch { }
            }

            
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                try
                {
                    var val = field.GetValue(mongoIdObj);
                    if (val != null)
                    {
                        string strVal = val.ToString();
                        if (strVal.Length == 24 && IsHexString(strVal))
                        {
                            return strVal;
                        }
                    }
                }
                catch { }
            }

            return idStr;
        }

        
        private static bool IsHexString(string str)
        {
            if (string.IsNullOrEmpty(str)) return false;
            foreach (char c in str)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }
        
        
        
        
        
        private static string ExtractItemId(object item, Type itemType)
        {
            foreach (var propName in new[] { "Id", "ItemId", "_id" })
            {
                var val = GetPropertyValue(item, itemType, propName) ?? GetFieldValue(item, itemType, propName);
                if (val != null)
                {
                    string idStr = ExtractMongoId(val) ?? val.ToString();
                    if (!string.IsNullOrEmpty(idStr) && idStr != itemType.Name)
                        return idStr;
                }
            }

            return "unknown";
        }

        
        
        
        
        
        
        
        
        private static string GetShortName(object item, Type itemType, string fallback)
        {
            
            string templateId = ExtractMongoId(GetPropertyValue(item, itemType, "TemplateId"));
            if (!string.IsNullOrEmpty(templateId) && templateId != "unknown")
            {
                string localeName = ResolveItemNameFromLocaleFile(templateId);
                if (!string.IsNullOrEmpty(localeName))
                    return localeName;
            }

            
            object template = GetPropertyValue(item, itemType, "Template");
            if (template != null)
            {
                var templateType = template.GetType();
                object templateShortName = GetPropertyValue(template, templateType, "ShortName");
                if (templateShortName != null)
                {
                    string result = TryGetLocalizedString(templateShortName, templateId);
                    if (!string.IsNullOrEmpty(result) && result != templateType.Name)
                        return result;
                }
            }

            
            object shortNameObj = GetPropertyValue(item, itemType, "ShortName");
            if (shortNameObj == null) return fallback;

            string sn = TryGetLocalizedString(shortNameObj, templateId);
            if (!string.IsNullOrEmpty(sn) && sn != itemType.Name)
                return sn;

            return fallback;
        }

        
        
        
        
        private static string ResolveItemNameFromLocaleFile(string templateId)
        {
            if (string.IsNullOrEmpty(templateId)) return null;

            if (_localeCache == null)
            {
                _localeCache = LoadLocaleFile();
                if (_localeCache == null)
                {
                    Log.LogWarning("[Locale] Файл локализации не найден");
                    return null;
                }
                Log.LogInfo($"[Locale] Загружено {_localeCache.Count} записей локализации");

                
                int dumpCount = 0;
                foreach (var kvp in _localeCache)
                {
                    if (kvp.Key.Contains("ShortName"))
                    {
                        Log.LogInfo($"[Locale] Sample key: '{kvp.Key}' -> '{kvp.Value}'");
                        dumpCount++;
                        if (dumpCount >= 5) break;
                    }
                }
            }

            
            string[] keys = {
            templateId + " ShortName",
            "templates " + templateId + " ShortName",
            templateId + " Name",
            "templates " + templateId + " Name",
        };

            foreach (var key in keys)
            {
                if (_localeCache.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
                {
                    Log.LogInfo($"[Locale] Найдено: '{key}' -> '{value}'");
                    return value;
                }
            }

            
            foreach (var kvp in _localeCache)
            {
                if (kvp.Key.EndsWith(templateId + " ShortName") && !string.IsNullOrEmpty(kvp.Value))
                {
                    Log.LogInfo($"[Locale] Найдено (суффикс): '{kvp.Key}' -> '{kvp.Value}'");
                    return kvp.Value;
                }
            }

            Log.LogInfo($"[Locale] Не найдено имя для templateId: {templateId}");
            return null;
        }

        
        private static Dictionary<string, string> LoadLocaleFile()
        {
            try
            {
                string gameRoot = AppDomain.CurrentDomain.BaseDirectory;
                Log.LogInfo("[Locale] Game root: " + gameRoot);

                string[] baseDirs = {
                Path.Combine(gameRoot, "SPT_Data", "database", "locales", "global"),
                Path.Combine(gameRoot, "SPT_Data", "Server", "database", "locales", "global"),
                Path.Combine(gameRoot, "SPT_Runtime", "SPT_Data", "database", "locales", "global"),
                Path.Combine(gameRoot, "Aki_Data", "Server", "database", "locales", "global"),
            };

                string[] languages = { "ru", "en" };

                foreach (var lang in languages)
                {
                    foreach (var dir in baseDirs)
                    {
                        string filePath = Path.Combine(dir, lang + ".json");
                        if (File.Exists(filePath))
                        {
                            Log.LogInfo("[Locale] Найден файл: " + filePath);
                            string json = File.ReadAllText(filePath);
                            return ParseLocaleJson(json);
                        }
                    }
                }

                
                try
                {
                    string[] files = Directory.GetFiles(gameRoot, "ru.json", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (file.Contains("locales") && file.Contains("global"))
                        {
                            Log.LogInfo("[Locale] Найден файл (поиск): " + file);
                            string json = File.ReadAllText(file);
                            return ParseLocaleJson(json);
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Log.LogWarning("[Locale] Ошибка загрузки: " + ex.Message);
            }

            return null;
        }

        
        
        private static Dictionary<string, string> ParseLocaleJson(string json)
        {
            var dict = new Dictionary<string, string>();
            try
            {
                int pos = 0;
                ParseJsonObject(json, ref pos, dict, "");
            }
            catch (Exception ex)
            {
                Log.LogWarning("[Locale] Ошибка парсинга JSON: " + ex.Message);
            }
            return dict.Count > 0 ? dict : null;
        }

        
        private static void ParseJsonObject(string json, ref int pos, Dictionary<string, string> dict, string prefix)
        {
            SkipJsonWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] != '{') return;
            pos++; 

            while (true)
            {
                SkipJsonWhitespace(json, ref pos);
                if (pos >= json.Length) return;
                if (json[pos] == '}') { pos++; return; }

                
                string key = ParseJsonString(json, ref pos);
                if (key == null) return;

                SkipJsonWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] != ':') return;
                pos++; 
                SkipJsonWhitespace(json, ref pos);

                string fullKey = string.IsNullOrEmpty(prefix) ? key : prefix + " " + key;

                if (pos < json.Length && json[pos] == '"')
                {
                    
                    string value = ParseJsonString(json, ref pos);
                    if (value != null)
                        dict[fullKey] = value;
                }
                else if (pos < json.Length && json[pos] == '{')
                {
                    
                    ParseJsonObject(json, ref pos, dict, fullKey);
                }
                else
                {
                    
                    SkipJsonValue(json, ref pos);
                }

                SkipJsonWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ',') pos++;
            }
        }

        
        private static string ParseJsonString(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"') return null;
            pos++; 
            var sb = new System.Text.StringBuilder();

            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '"')
                {
                    pos++; 
                    return sb.ToString();
                }
                if (c == '\\' && pos + 1 < json.Length)
                {
                    char next = json[pos + 1];
                    if (next == '"') sb.Append('"');
                    else if (next == '\\') sb.Append('\\');
                    else if (next == 'n') sb.Append('\n');
                    else if (next == 't') sb.Append('\t');
                    else if (next == 'r') sb.Append('\r');
                    else if (next == '/') sb.Append('/');
                    else if (next == 'u' && pos + 5 < json.Length)
                    {
                        string hex = json.Substring(pos + 2, 4);
                        try
                        {
                            int code = Convert.ToInt32(hex, 16);
                            sb.Append((char)code);
                            pos += 4;
                        }
                        catch
                        {
                            sb.Append(next);
                        }
                    }
                    else
                        sb.Append(next);
                    pos += 2;
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
            }
            return sb.ToString();
        }

        
        private static void SkipJsonWhitespace(string json, ref int pos)
        {
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    pos++;
                else
                    break;
            }
        }

        
        private static void SkipJsonValue(string json, ref int pos)
        {
            if (pos >= json.Length) return;
            char c = json[pos];
            if (c == '[')
            {
                int depth = 0;
                while (pos < json.Length)
                {
                    if (json[pos] == '[') depth++;
                    else if (json[pos] == ']') { depth--; if (depth == 0) { pos++; return; } }
                    pos++;
                }
            }
            else if (c == '{')
            {
                int depth = 0;
                while (pos < json.Length)
                {
                    if (json[pos] == '{') depth++;
                    else if (json[pos] == '}') { depth--; if (depth == 0) { pos++; return; } }
                    pos++;
                }
            }
            else
            {
                while (pos < json.Length)
                {
                    char ch = json[pos];
                    if (ch == ',' || ch == '}' || ch == ']') return;
                    pos++;
                }
            }
        }
        
        
        
        private static string TryGetLocalizedString(object obj, string templateId = null)
        {
            if (obj == null) return null;

            
            if (obj is string strKey)
            {
                if (!string.IsNullOrEmpty(templateId))
                {
                    string resolved = ResolveItemNameFromLocaleFile(templateId);
                    if (!string.IsNullOrEmpty(resolved))
                        return resolved;
                }
                return strKey;
            }

            var type = obj.GetType();

            
            string rawKey = null;
            try
            {
                var field = type.GetField("_stringID", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    var val = field.GetValue(obj);
                    if (val != null) rawKey = val.ToString();
                }
            }
            catch { }

            
            try
            {
                var localizedMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(m => m.Name == "Localized")
                    .ToList();

                foreach (var method in localizedMethods)
                {
                    try
                    {
                        var parms = method.GetParameters();
                        object result;
                        if (parms.Length == 0)
                            result = method.Invoke(obj, null);
                        else
                        {
                            object[] args = new object[parms.Length];
                            result = method.Invoke(obj, args);
                        }
                        if (result != null)
                        {
                            string str = result.ToString();
                            if (!string.IsNullOrEmpty(str)
                                && !str.Contains("LocalizedString")
                                && !str.Contains("MongoID"))
                                return str;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            
            try
            {
                var valProp = type.GetProperty("Value",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (valProp != null)
                {
                    var val = valProp.GetValue(obj);
                    if (val != null)
                    {
                        string str = val.ToString();
                        if (!string.IsNullOrEmpty(str)
                            && !str.Contains("LocalizedString")
                            && !str.Contains("MongoID"))
                            return str;
                    }
                }
            }
            catch { }

            
            if (!string.IsNullOrEmpty(templateId))
            {
                string resolved = ResolveItemNameFromLocaleFile(templateId);
                if (!string.IsNullOrEmpty(resolved))
                    return resolved;
            }

            
            if (!string.IsNullOrEmpty(rawKey))
                return rawKey;

            
            string s = obj.ToString();
            if (s.Contains("ShortName") || s.Contains("LocalizedString") || (s.Length == 24 && IsHexString(s)))
                return null;
            return s;
        }

        
        
        
        private static void SetFieldOrProp(object obj, Type type, string name, object value)
        {
            
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                object converted = ConvertValue(value, field.FieldType);
                field.SetValue(obj, converted);
                return;
            }

            
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
            {
                object converted = ConvertValue(value, prop.PropertyType);
                prop.SetValue(obj, converted);
                return;
            }
        }

        
        
        
        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;

            Type sourceType = value.GetType();

            if (targetType == sourceType) return value;

            
            if (targetType == typeof(int))
                return Convert.ToInt32(value);
            if (targetType == typeof(long))
                return Convert.ToInt64(value);
            if (targetType == typeof(double))
                return Convert.ToDouble(value);
            if (targetType == typeof(float))
                return Convert.ToSingle(value);
            if (targetType == typeof(string))
                return value.ToString();

            
            
            if (targetType.Name.Contains("String") && sourceType == typeof(string))
                return value.ToString();

            return value;
        }

        
        
        
        private static object GetPropertyValue(object obj, Type type, string propName)
        {
            try
            {
                var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (prop != null)
                    return prop.GetValue(obj);
            }
            catch { }
            return null;
        }

        
        
        
        private static object GetFieldValue(object obj, Type type, string fieldName)
        {
            try
            {
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                    return field.GetValue(obj);
            }
            catch { }
            return null;
        }

        
        
        
        
        private static object GetSingletonInstance(Type type)
        {
            try
            {
                var singletonType = typeof(Singleton<>).MakeGenericType(type);
                var instantiatedProp = singletonType.GetProperty("Instantiated", BindingFlags.Public | BindingFlags.Static);
                if (instantiatedProp != null && (bool)instantiatedProp.GetValue(null))
                {
                    var instanceProp = singletonType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instanceProp != null)
                        return instanceProp.GetValue(null);
                }
            }
            catch { }
            return null;
        }

        
        
        
        
        
        
        
        
        
        
        internal static void ShowNotification(string message)
        {
            
            try
            {
                Log.LogInfo("[Уведомление] " + message);
            }
            catch { }

            
            try
            {
                if (Singleton<NotificationManager>.Instantiated)
                {
                    var nm = Singleton<NotificationManager>.Instance;
                    if (nm != null)
                    {
                        
                        
                        var allMethods = typeof(NotificationManager).GetMethods(
                                BindingFlags.Public | BindingFlags.NonPublic |
                                BindingFlags.Static | BindingFlags.Instance)
                            .Where(m => m.Name == "DisplayMessageNotification")
                            .OrderByDescending(m => m.GetParameters().Length)
                            .ToList();

                        
                        
                        object colorArg = null;
                        try
                        {
                            var nullableType = typeof(Il2CppSystem.Nullable<>)
                                .MakeGenericType(typeof(UnityEngine.Color));
                            var ctor = nullableType.GetConstructor(new[] { typeof(UnityEngine.Color) });
                            if (ctor != null)
                                colorArg = ctor.Invoke(new object[] { UnityEngine.Color.white });
                        }
                        catch { }

                        foreach (var targetMethod in allMethods)
                        {
                            try
                            {
                                var targetParams = targetMethod.GetParameters();
                                object[] invokeArgs = new object[targetParams.Length];

                                for (int i = 0; i < targetParams.Length; i++)
                                {
                                    var pt = targetParams[i].ParameterType;
                                    if (pt == typeof(string))
                                        invokeArgs[i] = message;
                                    else if (pt == typeof(ENotificationDurationType)
                                             || pt.Name == "ENotificationDurationType")
                                        invokeArgs[i] = ENotificationDurationType.Long;
                                    else if (pt == typeof(ENotificationIconType)
                                             || pt.Name == "ENotificationIconType")
                                        invokeArgs[i] = ENotificationIconType.Default;
                                    else if (pt == typeof(bool))
                                        invokeArgs[i] = false;
                                    else if (pt.Name.Contains("Color") || pt.Name.Contains("Nullable"))
                                        invokeArgs[i] = colorArg;
                                    else
                                        invokeArgs[i] = null;
                                }

                                
                                object target = targetMethod.IsStatic ? null : nm;
                                targetMethod.Invoke(target, invokeArgs);
                                return;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        
        
        
        
        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType(fullName);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }
    }
    
    
    
    
    
    
    
    internal class FleaHelperBehaviour : MonoBehaviour
    {
        
        
        private bool _hotkeyWasPressed = false;

        
        private void Update()
        {
            
            if (FleaHelperPlugin.ChangeCurrencyKey.Value != KeyCode.None &&
                Input.GetKey(FleaHelperPlugin.ChangeCurrencyKey.Value))
            {
                if (!_hotkeyWasPressed)
                {
                    _hotkeyWasPressed = true;
                    FleaHelperPlugin.ChangeCurrency();
                }
            }

            
            if (Input.GetKey(FleaHelperPlugin.Hotkey.Value))
            {
                if (!_hotkeyWasPressed)
                {
                    _hotkeyWasPressed = true;
                    FleaHelperPlugin.PostItemToFlea();
                }
            }

            
            if (!Input.GetKey(FleaHelperPlugin.Hotkey.Value) &&
                (FleaHelperPlugin.ChangeCurrencyKey.Value == KeyCode.None ||
                 !Input.GetKey(FleaHelperPlugin.ChangeCurrencyKey.Value)))
            {
                _hotkeyWasPressed = false;
            }
        }
    }

    
    public enum EPostingCurrency
    {
        RUB,  
        USD,  
        EUR   
    }

} 






namespace FleaHelper.Patches
{
    [HarmonyPatch]
    internal class FleaHelperPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return Enumerable.Empty<MethodBase>();
        }
    }
}
