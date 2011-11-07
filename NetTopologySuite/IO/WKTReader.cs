using System;
//using System.Collections;
using System.Collections.Generic;
using System.IO;
using GeoAPI.Geometries;
using GeoAPI.IO;
using NetTopologySuite.Geometries;
using NetTopologySuite.Utilities;
using RTools_NTS.Util;

namespace NetTopologySuite.IO
{
    /// <summary>  
    /// Converts a Well-Known Text string to a <c>Geometry</c>.
    /// 
    /// The <c>WKTReader</c> allows
    /// extracting <c>Geometry</c> objects from either input streams or
    /// internal strings. This allows it to function as a parser to read <c>Geometry</c>
    /// objects from text blocks embedded in other data formats (e.g. XML). 
    /// 
    /// The Well-known
    /// Text format is defined in the <A HREF="http://www.opengis.org/techno/specs.htm">
    /// OpenGIS Simple Features Specification for SQL</A> . 
    /// 
    /// NOTE:  There is an inconsistency in the SFS. 
    /// The WKT grammar states that <c>MultiPoints</c> are represented by 
    /// <c>MULTIPOINT ( ( x y), (x y) )</c>, 
    /// but the examples show <c>MultiPoint</c>s as <c>MULTIPOINT ( x y, x y )</c>. 
    /// Other implementations follow the latter syntax, so NTS will adopt it as well.
    /// A <c>WKTReader</c> is parameterized by a <c>GeometryFactory</c>, 
    /// to allow it to create <c>Geometry</c> objects of the appropriate
    /// implementation. In particular, the <c>GeometryFactory</c> will
    /// determine the <c>PrecisionModel</c> and <c>SRID</c> that is used. 
    /// The <c>WKTReader</c> will convert the input numbers to the precise
    /// internal representation.
    /// <remarks>
    /// <see cref="WKTReader" /> reads also non-standard "LINEARRING" tags.
    /// </remarks>
    /// </summary>
    public class WKTReader : ITextGeometryReader
    {
        private IGeometryFactory _geometryFactory;
        private IPrecisionModel _precisionModel;
        int _index;

        private static readonly System.Globalization.CultureInfo InvariantCulture =
            System.Globalization.CultureInfo.InvariantCulture;
        private static readonly string NaNString = double.NaN.ToString(InvariantCulture); /*"NaN"*/

        /// <summary> 
        /// Creates a <c>WKTReader</c> that creates objects using a basic GeometryFactory.
        /// </summary>
        public WKTReader() : this(GeometryFactory.Default) { }

        /// <summary>  
        /// Creates a <c>WKTReader</c> that creates objects using the given
        /// <c>GeometryFactory</c>.
        /// </summary>
        /// <param name="geometryFactory">The factory used to create <c>Geometry</c>s.</param>
        public WKTReader(IGeometryFactory geometryFactory) 
        {
            _geometryFactory = geometryFactory;
            _precisionModel = geometryFactory.PrecisionModel;
        }

        public IGeometryFactory Factory
        {
            get { return _geometryFactory; }
            set
            {
                if (value != null)
                {
                    _geometryFactory = value;
                    _precisionModel = value.PrecisionModel;
                }
            }
        }

        /// <summary>
        /// Converts a Well-known Text representation to a <c>Geometry</c>.
        /// </summary>
        /// <param name="wellKnownText">
        /// one or more Geometry Tagged Text strings (see the OpenGIS
        /// Simple Features Specification) separated by whitespace.
        /// </param>
        /// <returns>
        /// A <c>Geometry</c> specified by <c>wellKnownText</c>
        /// </returns>
        public IGeometry Read(string wellKnownText) 
        {
            using (StringReader reader = new StringReader(wellKnownText))
            {
                return Read(reader);
            }            
        }

        public IGeometry Read(Stream stream)
        {
            throw new NotImplementedException();
        }

        /// <summary>  
        /// Converts a Well-known Text representation to a <c>Geometry</c>.
        /// </summary>
        /// <param name="reader"> 
        /// A Reader which will return a "Geometry Tagged Text"
        /// string (see the OpenGIS Simple Features Specification).
        /// </param>
        /// <returns>A <c>Geometry</c> read from <c>reader</c>.
        /// </returns>
        public IGeometry Read(TextReader reader)
        {
            var tokens = Tokenize(reader);
            /*
            StreamTokenizer tokenizer = new StreamTokenizer(reader);
            IList<Token> tokens = new List<Token>();
            tokenizer.Tokenize(tokens);     // Read directly all tokens
             */
            _index = 0;                      // Reset pointer to start of tokens
            try
            {
                return ReadGeometryTaggedText(tokens);
            }
            catch (IOException e)
            {
                throw new ParseException(e.ToString());
            }            
        }

        internal IList<Token> Tokenize(TextReader reader)
        {
            StreamTokenizer tokenizer = new StreamTokenizer(reader);
            IList<Token> tokens = new List<Token>();
            tokenizer.Tokenize(tokens);     // Read directly all tokens
            return tokens;
        }

        internal int Index { get { return _index; } set { _index = value; } }

		/// <summary>
		/// Returns the next array of <c>Coordinate</c>s in the stream.
		/// </summary>
		/// <param name="tokens">
		/// Tokenizer over a stream of text in Well-known Text
		/// format. The next element returned by the stream should be "(" (the
		/// beginning of "(x1 y1, x2 y2, ..., xn yn)") or "EMPTY".
		/// </param>
		/// <param name="skipExtraParenthesis">
		/// if set to <c>true</c> skip extra parenthesis around coordinates.
		/// </param>
		/// <returns>
		/// The next array of <c>Coordinate</c>s in the
		/// stream, or an empty array if "EMPTY" is the next element returned by
		/// the stream.
		/// </returns>
		private Coordinate[] GetCoordinates(IList<Token> tokens, Boolean skipExtraParenthesis)
		{
            string nextToken = GetNextEmptyOrOpener(tokens);
            if (nextToken.Equals("EMPTY")) 
                return new Coordinate[]{};
            List<Coordinate> coordinates = new List<Coordinate>();
			coordinates.Add(GetPreciseCoordinate(tokens, skipExtraParenthesis));
            nextToken = GetNextCloserOrComma(tokens);
            while (nextToken.Equals(",")) 
            {
				coordinates.Add(GetPreciseCoordinate(tokens, skipExtraParenthesis));
                nextToken = GetNextCloserOrComma(tokens);
            }
            return coordinates.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tokens"></param>
        /// <param name="skipExtraParenthesis"></param>
        /// <returns></returns>
        private Coordinate GetPreciseCoordinate(IList<Token> tokens, Boolean skipExtraParenthesis)
        {
            Coordinate coord = new Coordinate();
			Boolean extraParenthesisFound = false;
			if (skipExtraParenthesis)
			{
				extraParenthesisFound = IsStringValueNext(tokens, "(");
				if (extraParenthesisFound)
				{
					_index++;
				}
			}
			coord.X = GetNextNumber(tokens);
			coord.Y = GetNextNumber(tokens);
            if (IsNumberNext(tokens))
                coord.Z = GetNextNumber(tokens);

			if (skipExtraParenthesis && 
				extraParenthesisFound && 
				IsStringValueNext(tokens, ")"))
			{
					_index++;
			}

			_precisionModel.MakePrecise(coord);
            return coord;
        }

		private Boolean IsStringValueNext(IList<Token> tokens, String stringValue)
		{
			Token token = tokens[_index] /*as Token*/;
			return token.StringValue == stringValue;
		}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tokens"></param>
        /// <returns></returns>
        private bool IsNumberNext(IList<Token> tokens) 
        {
            Token token = tokens[_index] /*as Token*/;
            return token is FloatToken ||
                   token is IntToken ||
                   (token is WordToken && string.Compare(token.Object.ToString(), NaNString, StringComparison.InvariantCultureIgnoreCase) == 0);        
        }

        /// <summary>
        /// Returns the next number in the stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next token must be a number.
        /// </param>
        /// <returns>The next number in the stream.</returns>
        /// <exception cref="ParseException">if the next token is not a valid number</exception>
		private double GetNextNumber(IList<Token> tokens)
        {
            Token token = tokens[_index++] /*as Token*/;

            if (token == null)
                throw new ArgumentNullException("tokens", "Token list contains a null value");
            if (token is EofToken)
                throw new ParseException("Expected number but encountered end of stream");
            if (token is EolToken)
                throw new ParseException("Expected number but encountered end of line");
            if (token is FloatToken || token is IntToken)
                return (double) token.ConvertToType(typeof(double));
            if (token is WordToken)
            {
                if (string.Compare(token.Object.ToString(), NaNString, StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    return Double.NaN;
                }
                throw new ParseException("Expected number but encountered word: " + token.StringValue);
            }
            if (token.StringValue == "(")
                throw new ParseException("Expected number but encountered '('");
            if (token.StringValue == ")")
                throw new ParseException("Expected number but encountered ')'");
            if (token.StringValue == ",")
                throw new ParseException("Expected number but encountered ','");

            throw new ParseException("Expected number but encountered '" + token.StringValue + "'");
        }

        /// <summary>
        /// Returns the next "EMPTY" or "(" in the stream as uppercase text.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next token must be "EMPTY" or "(".
        /// </param>
        /// <returns>
        /// The next "EMPTY" or "(" in the stream as uppercase text.</returns>
        private string GetNextEmptyOrOpener(IList<Token> tokens) 
        {
            string nextWord = GetNextWord(tokens);
            if (nextWord.Equals("EMPTY") || nextWord.Equals("(")) 
                return nextWord;            
            throw new ParseException("Expected 'EMPTY' or '(' but encountered '" + nextWord + "'");
        }

        /// <summary>
        /// Returns the next ")" or "," in the stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next token must be ")" or ",".
        /// </param>
        /// <returns>
        /// The next ")" or "," in the stream.</returns>
        private string GetNextCloserOrComma(IList<Token> tokens) 
        {
            string nextWord = GetNextWord(tokens);
            if (nextWord.Equals(",") || nextWord.Equals(")")) 
                return nextWord;
            
            throw new ParseException("Expected ')' or ',' but encountered '" + nextWord
                + "'");
        }

        /// <summary>
        /// Returns the next ")" in the stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next token must be ")".
        /// </param>
        /// <returns>
        /// The next ")" in the stream.</returns>
        private string GetNextCloser(IList<Token> tokens) 
        {
            string nextWord = GetNextWord(tokens);    
            if (nextWord.Equals(")"))
                return nextWord;
            throw new ParseException("Expected ')' but encountered '" + nextWord + "'");         
        }

        /// <summary>
        /// Returns the next word in the stream as uppercase text.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next token must be a word.
        /// </param>
        /// <returns>The next word in the stream as uppercase text.</returns>
        private string GetNextWord(IList<Token> tokens)
        {
            Token token = tokens[_index++] /*as Token*/;

            if (token is EofToken)
                throw new ParseException("Expected number but encountered end of stream");
            if (token is EolToken)
                throw new ParseException("Expected number but encountered end of line");
            if (token is FloatToken || token is IntToken)
                throw new ParseException("Expected word but encountered number: " + token.StringValue);
            if (token is WordToken)
                return token.StringValue.ToUpper();
            if (token.StringValue == "(")
                return "(";
            if (token.StringValue == ")")
                return ")";
            if (token.StringValue == ",")
                return ",";
            
            Assert.ShouldNeverReachHere();
            return null;
        }

        /// <summary>
        /// Creates a <c>Geometry</c> using the next token in the stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next tokens must form a &lt;Geometry Tagged Text.
        /// </param>
        /// <returns>A <c>Geometry</c> specified by the next token
        /// in the stream.</returns>
        internal IGeometry ReadGeometryTaggedText(IList<Token> tokens) 
        {            
            /*
             * A new different implementation by Marc Jacquin:
             * this code manages also SRID values.
             */
            IGeometry returned;
            string sridValue = null;
            string type = tokens[_index].ToString();
            
            if (type == "SRID") 
            {
                sridValue = tokens[2].ToString();
                // tokens.RemoveRange(0, 4);
                tokens.RemoveAt(0);
                tokens.RemoveAt(0);
                tokens.RemoveAt(0);
                tokens.RemoveAt(0);
            }
            else type = GetNextWord(tokens);
            if (type.Equals("POINT"))
                returned = ReadPointText(tokens);            
            else if (type.Equals("LINESTRING"))
                returned =  ReadLineStringText(tokens);            
            else if (type.Equals("LINEARRING"))
                returned =  ReadLinearRingText(tokens);            
            else if (type.Equals("POLYGON"))
                returned =  ReadPolygonText(tokens);            
            else if (type.Equals("MULTIPOINT"))
                returned =  ReadMultiPointText(tokens);
            else if (type.Equals("MULTILINESTRING")) 
                returned =  ReadMultiLineStringText(tokens);            
            else if (type.Equals("MULTIPOLYGON"))
                returned =  ReadMultiPolygonText(tokens);            
            else if (type.Equals("GEOMETRYCOLLECTION"))
                returned =  ReadGeometryCollectionText(tokens);
            else throw new ParseException("Unknown type: " + type);

            if (returned == null)
                throw new NullReferenceException("Error reading geometry");

            if (sridValue != null)            
                returned.SRID = Convert.ToInt32(sridValue);

            return returned;                        
        }

        /// <summary>
        /// Creates a <c>Point</c> using the next token in the stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next tokens must form a &lt;Point Text.
        /// </param>
        /// <returns>A <c>Point</c> specified by the next token in
        /// the stream.</returns>
        private IPoint ReadPointText(IList<Token> tokens) 
        {
            string nextToken = GetNextEmptyOrOpener(tokens);
            if (nextToken.Equals("EMPTY")) 
                return _geometryFactory.CreatePoint((Coordinate) null);
            IPoint point = _geometryFactory.CreatePoint(GetPreciseCoordinate(tokens, false));
            GetNextCloser(tokens);
            return point;
        }

        /// <summary>
        /// Creates a <c>LineString</c> using the next token in the stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next tokens must form a &lt;LineString Text.
        /// </param>
        /// <returns>
        /// A <c>LineString</c> specified by the next
        /// token in the stream.</returns>
        private ILineString ReadLineStringText(IList<Token> tokens) 
        {
            return _geometryFactory.CreateLineString(GetCoordinates(tokens, false));
        }

        /// <summary>
        /// Creates a <c>LinearRing</c> using the next token in the stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next tokens must form a &lt;LineString Text.
        /// </param>
        /// <returns>A <c>LinearRing</c> specified by the next
        /// token in the stream.</returns>
        private ILinearRing ReadLinearRingText(IList<Token> tokens)
        {
			return _geometryFactory.CreateLinearRing(GetCoordinates(tokens, false));
        }

        /// <summary>
        /// Creates a <c>MultiPoint</c> using the next token in the stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next tokens must form a &lt;MultiPoint Text.
        /// </param>
        /// <returns>
        /// A <c>MultiPoint</c> specified by the next
        /// token in the stream.</returns>
        private IMultiPoint ReadMultiPointText(IList<Token> tokens) 
        {
            return _geometryFactory.CreateMultiPoint(ToPoints(GetCoordinates(tokens, true)));
        }

        /// <summary> 
        /// Creates an array of <c>Point</c>s having the given <c>Coordinate</c>s.
        /// </summary>
        /// <param name="coordinates">
        /// The <c>Coordinate</c>s with which to create the <c>Point</c>s
        /// </param>
        /// <returns>
        /// <c>Point</c>s created using this <c>WKTReader</c>
        /// s <c>GeometryFactory</c>.
        /// </returns>
        private IPoint[] ToPoints(Coordinate[] coordinates) 
        {
            List<IPoint> points = new List<IPoint>();
            for (int i = 0; i < coordinates.Length; i++) 
                points.Add(_geometryFactory.CreatePoint(coordinates[i]));            
            return points.ToArray();
        }
        
        /// <summary>  
        /// Creates a <c>Polygon</c> using the next token in the stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next tokens must form a Polygon Text.
        /// </param>
        /// <returns>
        /// A <c>Polygon</c> specified by the next token
        /// in the stream.        
        /// </returns>
        private IPolygon ReadPolygonText(IList<Token> tokens) 
        {
            string nextToken = GetNextEmptyOrOpener(tokens);
            if (nextToken.Equals("EMPTY")) 
                return _geometryFactory.CreatePolygon(
                    _geometryFactory.CreateLinearRing(new Coordinate[] { } ), new ILinearRing[] { } );

            List<ILinearRing> holes = new List<ILinearRing>();
            ILinearRing shell = ReadLinearRingText(tokens);
            nextToken = GetNextCloserOrComma(tokens);
            while (nextToken.Equals(",")) 
            {
                ILinearRing hole = ReadLinearRingText(tokens);
                holes.Add(hole);
                nextToken = GetNextCloserOrComma(tokens);
            }
            return _geometryFactory.CreatePolygon(shell, holes.ToArray());
        }

        /// <summary>
        /// Creates a <c>MultiLineString</c> using the next token in the stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next tokens must form a MultiLineString Text.
        /// </param>
        /// <returns>
        /// A <c>MultiLineString</c> specified by the
        /// next token in the stream.</returns>
        private IMultiLineString ReadMultiLineStringText(IList<Token> tokens) 
        {
            string nextToken = GetNextEmptyOrOpener(tokens);
            if (nextToken.Equals("EMPTY")) 
                return _geometryFactory.CreateMultiLineString( new ILineString[] { } );

            List<ILineString> lineStrings = new List<ILineString>();
            ILineString lineString = ReadLineStringText(tokens);
            lineStrings.Add(lineString);
            nextToken = GetNextCloserOrComma(tokens);
            while (nextToken.Equals(",")) 
            {
                lineString = ReadLineStringText(tokens);
                lineStrings.Add(lineString);
                nextToken = GetNextCloserOrComma(tokens);
            }            
            return _geometryFactory.CreateMultiLineString(lineStrings.ToArray());
        }

        /// <summary>  
        /// Creates a <c>MultiPolygon</c> using the next token in the stream.
        /// </summary>
        /// <param name="tokens">Tokenizer over a stream of text in Well-known Text
        /// format. The next tokens must form a MultiPolygon Text.
        /// </param>
        /// <returns>
        /// A <c>MultiPolygon</c> specified by the next
        /// token in the stream, or if if the coordinates used to create the
        /// <c>Polygon</c> shells and holes do not form closed linestrings.</returns>
        private IMultiPolygon ReadMultiPolygonText(IList<Token> tokens) 
        {
            string nextToken = GetNextEmptyOrOpener(tokens);
            if (nextToken.Equals("EMPTY")) 
                return _geometryFactory.CreateMultiPolygon(new IPolygon[]{});

            List<IPolygon> polygons = new List<IPolygon>();
            IPolygon polygon = ReadPolygonText(tokens);
            polygons.Add(polygon);
            nextToken = GetNextCloserOrComma(tokens);
            while (nextToken.Equals(",")) 
            {
                polygon = ReadPolygonText(tokens);
                polygons.Add(polygon);
                nextToken = GetNextCloserOrComma(tokens);
            }            
            return _geometryFactory.CreateMultiPolygon(polygons.ToArray());
        }

        /// <summary>
        /// Creates a <c>GeometryCollection</c> using the next token in the
        /// stream.
        /// </summary>
        /// <param name="tokens">
        /// Tokenizer over a stream of text in Well-known Text
        /// format. The next tokens must form a &lt;GeometryCollection Text.
        /// </param>
        /// <returns>
        /// A <c>GeometryCollection</c> specified by the
        /// next token in the stream.</returns>
        private IGeometryCollection ReadGeometryCollectionText(IList<Token> tokens) 
        {
            string nextToken = GetNextEmptyOrOpener(tokens);
            if (nextToken.Equals("EMPTY")) 
                return _geometryFactory.CreateGeometryCollection(new IGeometry[] { } );

            List<IGeometry> geometries = new List<IGeometry>();
            IGeometry geometry = ReadGeometryTaggedText(tokens);
            geometries.Add(geometry);
            nextToken = GetNextCloserOrComma(tokens);
            while (nextToken.Equals(",")) 
            {
                geometry = ReadGeometryTaggedText(tokens);
                geometries.Add(geometry);
                nextToken = GetNextCloserOrComma(tokens);
            }            
            return _geometryFactory.CreateGeometryCollection(geometries.ToArray());
        }

        #region Implementation of IGeometryIOSettings

        public bool HandleSRID
        {
            get { return true; }
            set { }
        }

        public Ordinates AllowedOrdinates
        {
            get { return Ordinates.XYZ; }
        }

        public Ordinates HandleOrdinates
        {
            get { return AllowedOrdinates; }
            set { }
        }

        #endregion
    }    
}
