using System;
using System.Linq;

namespace CirithSim
{
    // Optional BannerlordVR support: when the BannerlordVR module is loaded (or CIRITH_VR=1) the unique animations and the proxy sword are OFF, the weapon is a normal engine weapon in the right hand
    // (visible item cirith_sword_vr), sprint (key C, x1.5) and the jiggle physics (with VR safety limits) still work.  CIRITH_VR=0 forces it off.
    public static class CirithVR
    {
        static int state = -1;
        public static bool Active
        {
            get
            {
                if (state < 0)
                {
                    var e = Environment.GetEnvironmentVariable("CIRITH_VR");
                    bool on = e == "1" || (e != "0" && AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BannerlordVR"));
                    state = on ? 1 : 0; SimSubModule.L("VR support: " + (on ? "ON (BannerlordVR detected) - unique animations off, normal right-hand weapon, jiggle with VR safety limits" : "off"));
                }
                return state == 1;
            }
        }
    }
}
