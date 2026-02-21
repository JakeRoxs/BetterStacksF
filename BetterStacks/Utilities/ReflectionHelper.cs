using System;


namespace BetterStacks.Utilities
{
    internal static class ReflectionHelper
    {
        /// <summary>
        /// Attempts to get a <c>StackLimit</c> property or field value from an object.
        /// </summary>
        /// <returns><c>true</c> when a value was read successfully.</returns>
        public static bool TryGetStackLimit(object def, out int currentStack)
        {
            currentStack = 0;
            var defType = def.GetType();
            var prop = defType.GetProperty("StackLimit");
            var field = prop == null ? defType.GetField("StackLimit") : null;
            if ((prop == null || !prop.CanRead) && field == null)
                return false;
            try
            {
                if (prop != null)
                    currentStack = Convert.ToInt32(prop.GetValue(def));
                else if (field != null)
                    currentStack = Convert.ToInt32(field.GetValue(def));
                return true;
            }
            catch (Exception ex)
            {
                // include the exception type to make post‑mortem diagnostics easier
                LoggingHelper.Warning($"TryGetStackLimit reflection failed ({ex.GetType().Name}): {ex.Message}");
                return false;
            }
        }

        public static bool TrySetStackLimit(object def, int value)
        {
            var defType = def.GetType();
            var prop = defType.GetProperty("StackLimit");
            var field = prop == null ? defType.GetField("StackLimit") : null;
            try
            {
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(def, value);
                    return true;
                }
                else if (field != null)
                {
                    field.SetValue(def, value);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning($"TrySetStackLimit reflection failed ({ex.GetType().Name}): {ex.Message}");
            }
            return false;
        }
    }
}
