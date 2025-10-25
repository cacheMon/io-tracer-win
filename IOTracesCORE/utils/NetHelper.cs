using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.utils
{
    internal class NetHelper
    {
        static bool IsPrivate(IPAddress ip)
        {
            /*
                Filters local traffic
                IPV4:
                10.0.0.0/8
                172.16.0.0/12
                192.168.0.0/16
                169.254.0.0/16 (Link-local address)

                IPV6:
                fc00::/7 
                fe80::/10
             */
            if (ip == null) return false;

            if (IPAddress.IsLoopback(ip))
                return true;

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                byte[] b = ip.GetAddressBytes();
                if (b[0] == 10)
                    return true;

                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    return true;

                if (b[0] == 192 && b[1] == 168)
                    return true;

                if (b[0] == 169 && b[1] == 254)
                    return true;

                return false;
            }

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                byte[] b = ip.GetAddressBytes();

                if (b[0] == 0xfc || b[0] == 0xfd)
                    return true;

                if (b[0] == 0xfe && (b[1] & 0xC0) == 0x80) 
                    return true;

                return false;
            }

            return false;
        }

        public static bool IsLocalConversation(IPAddress src, IPAddress dst)
        {
            return IsPrivate(src) && IsPrivate(dst);
        }
    }
}
