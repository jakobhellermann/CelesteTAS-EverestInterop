using System;
using System.Collections.Generic;

namespace TAS.InfoHUD;

internal interface ITargetQueryHost {
    TargetQuery.Handler[] Handlers { get; }
    IEnumerable<Type> GetTypes();
    string GetModName(Type type);
    void LogError(string message);
    void LogException(Exception exception, string message);
}
