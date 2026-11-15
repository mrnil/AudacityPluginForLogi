namespace Loupedeck.AudacityPlugin
{
    using System;

    public class AudacityApplication : ClientApplication
    {
        // Windows: Audacity.exe
        protected override String GetProcessName() => "Audacity";

        // macOS. VERIFY on your Mac: osascript -e 'id of app "Audacity"'
        protected override String GetBundleName() => "org.audacityteam.audacity";
    }
}
