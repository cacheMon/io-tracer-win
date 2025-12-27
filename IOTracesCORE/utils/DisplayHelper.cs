using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.utils
{
    internal class DisplayHelper
    {
        public static string ToPowerOfTen(long count)
        {
            if (count < 1_000_000)
                return count.ToString("N0");

            int exponent = (int)Math.Floor(Math.Log10(count));
            double mantissa = count / Math.Pow(10, exponent);

            return $"{mantissa:F2} × 10{ToSuperscript(exponent)}";
        }

        public static string ToSuperscript(int number)
        {
            string str = number.ToString();
            string result = "";
            foreach (char c in str)
            {
                result += c switch
                {
                    '0' => '⁰',
                    '1' => '¹',
                    '2' => '²',
                    '3' => '³',
                    '4' => '⁴',
                    '5' => '⁵',
                    '6' => '⁶',
                    '7' => '⁷',
                    '8' => '⁸',
                    '9' => '⁹',
                    '-' => '⁻',
                    _ => c
                };
            }
            return result;
        }
    }
}
