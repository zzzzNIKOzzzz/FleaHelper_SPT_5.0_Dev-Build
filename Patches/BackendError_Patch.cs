using System.Collections.Generic;
using System.Reflection;
using System.Linq; // <--- Добавь эту строку
using HarmonyLib;

namespace FleaHelper.Patches
{
    [HarmonyPatch]
    internal class BackendError_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            // 1. Перебираем все методы в PreloaderUI с именем ShowErrorScreen
            var preloaderType = AccessTools.TypeByName("EFT.UI.PreloaderUI");
            if (preloaderType != null)
            {
                foreach (var method in preloaderType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (method.Name == "ShowErrorScreen")
                    {
                        FleaHelperPlugin.Log.LogInfo($"[FleaHelper] Найден метод для патча: PreloaderUI.ShowErrorScreen({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                        yield return method;
                    }
                }
            }

            // 2. Перебираем все методы в MessageWindow с именем Show
            var messageWindowType = AccessTools.TypeByName("EFT.UI.MessageWindow");
            if (messageWindowType != null)
            {
                foreach (var method in messageWindowType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (method.Name == "Show")
                    {
                        FleaHelperPlugin.Log.LogInfo($"[FleaHelper] Найден метод для патча: MessageWindow.Show({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                        yield return method;
                    }
                }
            }
        }

        // Prefix выполняется ДО того, как игра покажет окно
        static bool Prefix(object[] __args)
        {
            try
            {
                // Перебираем все аргументы метода (обычно это title и message)
                foreach (var arg in __args)
                {
                    if (arg is string str)
                    {
                        // Если в тексте окна есть упоминание барахолки или нашей ошибки
                        if (str.Contains("Unable to trade") || 
                            str.Contains("flea market") || 
                            str.Contains("before level"))
                        {
                            FleaHelperPlugin.Log.LogInfo("[FleaHelper] Перехвачено окно ошибки бэкенда: " + str);
                            
                            // Показываем наше уведомление
                            FleaHelperPlugin.ShowNotification("Предмет невозможно продать (ограничение сервера)");
                            
                            // Возвращаем false, чтобы ОТМЕНИТЬ показ стандартного черного окна
                            return false; 
                        }
                    }
                }
            }
            catch { }
            
            // Если это какая-то другая ошибка, пропускаем её (показываем стандартное окно)
            return true;
        }
    }
}