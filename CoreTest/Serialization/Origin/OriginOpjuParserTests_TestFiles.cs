using System;
using System.IO;

namespace Altaxo.Serialization.Origin
{
    public class OriginOpjuParserTests_TestFiles
    {
        public string TestFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Serialization\Origin\TestFiles");



        private static DateTime RoundToSecond(DateTime value, int decimals)
        {
            var ticksPerSecond = TimeSpan.TicksPerSecond;
            long scale = 1;
            for (int i = 0; i < decimals; ++i)
                scale *= 10;

            return new DateTime((ticksPerSecond / scale) * ((scale * value.Ticks + (ticksPerSecond / 2)) / ticksPerSecond), value.Kind);
        }

        private static TimeSpan RoundToSecond(TimeSpan value, int decimals)
        {
            var ticksPerSecond = TimeSpan.TicksPerSecond;
            long scale = 1;
            for (int i = 0; i < decimals; ++i)
                scale *= 10;

            return new TimeSpan((ticksPerSecond / scale) * ((scale * value.Ticks + (ticksPerSecond / 2)) / ticksPerSecond));
        }
    }
}
