﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

#if SILVERLIGHT && !WINDOWS_PHONE
using NetTopologySuite.Encodings;
using System.Linq;
#endif

#if NoLinq

namespace System
{
    public delegate TResult Func<T1, TResult>(T1 t1);
    public delegate TResult Func<T1, T2, TResult>(T1 t1, T2 t2);
    public delegate TResult Func<T1, T2, T3, TResult>(T1 t1, T2 t2, T3 t3);
    public delegate TResult Func<T1, T2, T3, T4, TResult>(T1 t1, T2 t2, T3 t3, T4 t4);

    namespace Collections.Generic
    {
        public static class Enumerable
        {
            public static T[] ToArray<T>(IEnumerable<T> enumerable)
            {
                var asList = enumerable as List<T> ?? new List<T>(enumerable);
                return asList.ToArray();
            }

            public static TOut[] ToArray<TOut>(IEnumerable enumerable)
            {
                var res = new List<TOut>();
                foreach (var @in in enumerable)
                    res.Add((TOut)@in);
                return res.ToArray();
            }
        }
    }
}
#endif

namespace NetTopologySuite.Utilities
{
    public static class PlatformUtilityEx
    {
#if SILVERLIGHT && !WINDOWS_PHONE

        [Obsolete("Not used anywhere within NTS")]
        private static readonly IEncodingRegistry Registry = new EncodingRegistry();

        public static IEnumerable<object> CastPlatform(this ICollection self)
        {
            return self.Cast<object>();
        }

        public static IEnumerable<object> CastPlatform(this IList self)
        {
            return self.Cast<object>();
        }

        public static IEnumerable<T> CastPlatform<T>(this IList<T> self)
        {
            return self;
        }

        [Obsolete("Not used anywhere within NTS")]
        public static Encoding GetDefaultEncoding()
        {
            return Encoding.Unicode;
        }

        [Obsolete("Not used anywhere within NTS")]
        public static Encoding GetASCIIEncoding()
        {
            return Registry.ASCII;
        }

#else

        public static ICollection CastPlatform(ICollection self)
        {
            return self;
        }

        public static ICollection CastPlatform(IList self)
        {
            return self;
        }

        public static ICollection<T> CastPlatform<T>(IList<T> self)
        {
            return self;
        }

#if !WINDOWS_PHONE

        [Obsolete("Not used anywhere within NTS")]
        public static Encoding GetDefaultEncoding()
        {
            return Encoding.Default;
        }

        [Obsolete("Not used anywhere within NTS")]
        public static Encoding GetASCIIEncoding()
        {
            return new ASCIIEncoding();
        }
#else

        [Obsolete("Not used anywhere within NTS")]
        public static Encoding GetDefaultEncoding()
        {
            return Encoding.UTF8;
        }

#endif

#endif
    }
}