using System;
using System.Collections.Generic;

namespace TAS.InfoHUD;

public interface ITargetQueryHost {
    IEnumerable<Type> GetTypes();
    string GetModName(Type type);
    void LogError(string message);
    void LogException(Exception exception, string message);
}
