using System;
using System.Collections;
using System.Collections.Generic;
using GeoAPI.Geometries;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Noding;
using NetTopologySuite.Noding.Snapround;
using NUnit.Framework;

namespace NetTopologySuite.Tests.NUnit.Noding.Snaparound
{
    /// <summary>
    /// Test Snap Rounding
    /// </summary>
    public class SnapRoundingTest
    {
        WKTReader rdr = new WKTReader();

        [Test]
        public void TestPolyWithCloseNode()
        {
            string[] polyWithCloseNode = {
                "POLYGON ((20 0, 20 160, 140 1, 160 160, 160 1, 20 0))"
                };
            RunRounding(polyWithCloseNode);
        }

        [Test]
        public void TestLineStringLongShort()
        {
            String[] geoms = {
                                 "LINESTRING (0 0, 2 0)",
                                 "LINESTRING (0 0, 10 -1)"
                             };
            RunRounding(geoms);
        }


        [Test]
        public void TestBadLines1() {
            string[] badLines1 = {
                "LINESTRING ( 171 157, 175 154, 170 154, 170 155, 170 156, 170 157, 171 158, 171 159, 172 160, 176 156, 171 156, 171 159, 176 159, 172 155, 170 157, 174 161, 174 156, 173 156, 172 156 )"
                };
            RunRounding(badLines1);
        }

        [Test]
        public void TestBadLines2() {
            string[] badLines2 = {
                "LINESTRING ( 175 222, 176 222, 176 219, 174 221, 175 222, 177 220, 174 220, 174 222, 177 222, 175 220, 174 221 )"
                };
            RunRounding(badLines2);
        }

        [Test]
        public void TestCollapse1() {
            string[] collapse1 = {
                "LINESTRING ( 362 177, 375 164, 374 164, 372 161, 373 163, 372 165, 373 164, 442 58 )"
                };
            RunRounding(collapse1);
        }

        [Test]
        public void TestBadNoding1() {
            string[] badNoding1 = {
                "LINESTRING ( 76 47, 81 52, 81 53, 85 57, 88 62, 89 64, 57 80, 82 55, 101 74, 76 99, 92 67, 94 68, 99 71, 103 75, 139 111 )"
                };
            RunRounding(badNoding1);
        }

        [Test]
        public void TestBadNoding1Extract() {
            string[] badNoding1Extract = {
                "LINESTRING ( 82 55, 101 74 )",
                "LINESTRING ( 94 68, 99 71 )",
                "LINESTRING ( 85 57, 88 62 )"
                };
            RunRounding(badNoding1Extract);
        }
        [Test]
        public void TestBadNoding1ExtractShift() {
            string[] badNoding1ExtractShift = {
                "LINESTRING ( 0 0, 19 19 )",
                "LINESTRING ( 12 13, 17 16 )",
                "LINESTRING ( 3 2, 6 7 )"
                };
            RunRounding(badNoding1ExtractShift);
        }


        private const double SnapTolerance = 1.0;

        void RunRounding(string[] wkt)
        {
            var geoms = FromWKT(wkt);
            PrecisionModel pm = new PrecisionModel(SnapTolerance);
            GeometryNoder noder = new GeometryNoder(pm);
            noder.IsValidityChecked = true;
            var nodedLines = noder.Node(geoms);
            
            foreach ( var ls in nodedLines)
                Console.WriteLine(ls);

            Assert.IsTrue(IsSnapped(nodedLines, SnapTolerance));
            
        }

        ICollection<IGeometry> FromWKT(string[] wkts)
        {
            ICollection<IGeometry> geomList = new List<IGeometry>();
            for (int i = 0; i < wkts.Length; i++)
            {
                try {
                    geomList.Add(rdr.Read(wkts[i]));
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.StackTrace);
                }
            }
            return geomList;
        }

        private static bool IsSnapped(IList<ILineString> lines, double tol)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                for (var j = 0; j < line.NumPoints; j++)
                {
                    var v = line.GetCoordinateN(j);
                    if (!IsSnapped(v, lines)) return false;

                }
            }
            return true;
        }

        private static bool IsSnapped(Coordinate v, IList<ILineString> lines)
        {
            for (var i = 0; i < lines.Count ; i++)
            {
                var line = lines[i];
                for (var j = 0; j < line.NumPoints - 1; j++)
                {
                    var p0 = line.GetCoordinateN(j);
                    var p1 = line.GetCoordinateN(j);
                    if (!IsSnapped(v, p0, p1)) return false;
                }
            }
            return true;
        }

        private static bool IsSnapped(Coordinate v, Coordinate p0, Coordinate p1)
        {
            if (v.Equals2D(p0)) return true;
            if (v.Equals2D(p1)) return true;
            var seg = new LineSegment(p0, p1);
            var dist = seg.Distance(v);
            if (dist < SnapTolerance / 2.05) return false;
            return true;
        }
    }
}