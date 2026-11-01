using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace FleaHelper.Patches
{
    [HarmonyPatch]
    internal class HTTPTransportManager_ExtractResponseResult_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var type = AccessTools.TypeByName("HTTPTransportManager") 
                       ?? AccessTools.TypeByName("Comfort.Common.HTTPTransportManager");

            if (type != null)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (method.Name == "ExtractResponseResult" && method.IsGenericMethod)
                    {
                        yield return method;
                    }
                }
            }
            else
            {
                FleaHelperPlugin.Log.LogWarning("[FleaHelper] HTTPTransportManager не найден для патча");
            }
        }

        static void Postfix(string responseJsonText)
        {
            try
            {
                if (responseJsonText != null && 
                    (responseJsonText.Contains("Unable to trade") || 
                     responseJsonText.Contains("before level -1")))
                {
                    FleaHelperPlugin.ShowNotification("Предмет невозможно продать (ограничение сервера)");
                }
            }
            catch { /* Игнорируем ошибки в патче, чтобы не сломать игру */ }
        }
    }
}