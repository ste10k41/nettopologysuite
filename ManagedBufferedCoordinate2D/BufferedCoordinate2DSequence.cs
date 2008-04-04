﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GeoAPI.Coordinates;
using GeoAPI.DataStructures.Collections.Generic;
using GeoAPI.Geometries;
using GeoAPI.Utilities;
using NPack;
using NPack.Interfaces;

namespace NetTopologySuite.Coordinates
{
    using IBufferedCoordFactory = ICoordinateFactory<BufferedCoordinate2D>;
    using IBufferedCoordSequence = ICoordinateSequence<BufferedCoordinate2D>;
    using IBufferedCoordSequenceFactory = ICoordinateSequenceFactory<BufferedCoordinate2D>;

    public class BufferedCoordinate2DSequence : IBufferedCoordSequence
    {
        private enum SequenceStorage
        {
            Unknown = 0,
            PrependList,
            MainList,
            AppendList
        }

        private readonly IVectorBuffer<BufferedCoordinate2D, DoubleComponent> _buffer;
        private readonly BufferedCoordinate2DSequenceFactory _factory;
        private List<Int32> _sequence;
        private Boolean _reversed;
        private Int32 _startIndex = -1;
        private Int32 _endIndex = -1;
        private List<Int32> _appendedIndexes;
        private List<Int32> _prependedIndexes;
        private SortedSet<Int32> _skipIndexes;
        private Boolean _isFrozen;
        private Int32 _max = -1;
        private Int32 _min = -1;

        internal BufferedCoordinate2DSequence(BufferedCoordinate2DSequenceFactory factory,
                                              IVectorBuffer<BufferedCoordinate2D, DoubleComponent> buffer)
            : this(0, factory, buffer) { }

        internal BufferedCoordinate2DSequence(Int32 size, BufferedCoordinate2DSequenceFactory factory,
                                              IVectorBuffer<BufferedCoordinate2D, DoubleComponent> buffer)
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size", size,
                                                                 "Size should be greater " +
                                                                 "than 0");
            _factory = factory;
            _buffer = buffer;
            _sequence = new List<Int32>(Math.Max(size, 8));

            for (Int32 i = 0; i < size; i++)
            {
                _sequence.Add(-1);
            }
        }

        internal BufferedCoordinate2DSequence(List<Int32> sequence,
                                              BufferedCoordinate2DSequenceFactory factory,
                                              IVectorBuffer<BufferedCoordinate2D, DoubleComponent> buffer)
            : this(sequence, null, null, factory, buffer) { }

        internal BufferedCoordinate2DSequence(List<Int32> sequence,
                                              Int32? startIndex,
                                              Int32? endIndex,
                                              BufferedCoordinate2DSequenceFactory factory,
                                              IVectorBuffer<BufferedCoordinate2D, DoubleComponent> buffer)
        {
            _startIndex = startIndex ?? _startIndex;
            _endIndex = endIndex ?? _endIndex;
            _factory = factory;
            _buffer = buffer;
            _sequence = sequence;
        }

        #region IBufferedCoordSequence Members

        public IBufferedCoordSequence Append(BufferedCoordinate2D coordinate)
        {
            if (coordinate.IsEmpty)
            {
                throw new ArgumentException("Coordinate cannot be empty.");
            }

            Int32 coordIndex = coordinate.Index;
            appendCoordIndex(coordIndex);
            return this;
        }

        public IBufferedCoordSequence Append(IEnumerable<BufferedCoordinate2D> coordinates)
        {
            BufferedCoordinate2DSequence seq = coordinates as BufferedCoordinate2DSequence;

            if (seq != null)
            {
                appendInternal(seq);
            }
            else
            {
                foreach (BufferedCoordinate2D coordinate in coordinates)
                {
                    Add(coordinate);
                }
            }

            return this;
        }

        public IBufferedCoordSequence Append(IBufferedCoordSequence coordinates)
        {
            BufferedCoordinate2DSequence seq = coordinates as BufferedCoordinate2DSequence;

            if (seq != null)
            {
                appendInternal(seq);
                return this;
            }

            return Append((IEnumerable<BufferedCoordinate2D>)coordinates);
        }

        public IBufferedCoordSequenceFactory CoordinateSequenceFactory
        {
            get { return _factory; }
        }

        public BufferedCoordinate2D[] ToArray()
        {
            BufferedCoordinate2D[] array = new BufferedCoordinate2D[Count];

            for (Int32 i = 0; i < Count; i++)
            {
                Int32 coordIndex = _sequence[i];
                BufferedCoordinate2D coord = _buffer[coordIndex];
                array[i] = coord;
            }

            return array;
        }

        public void Add(BufferedCoordinate2D item)
        {
            checkFrozen();

            addInternal(item);
            OnSequenceChanged();
        }

        public IBufferedCoordSequence AddRange(IEnumerable<BufferedCoordinate2D> coordinates,
                                               Boolean allowRepeated,
                                               Boolean reverse)
        {
            checkFrozen();

            if (reverse)
            {
                coordinates = Enumerable.Reverse(coordinates);
            }

            Int32 lastIndex = -1;

            foreach (BufferedCoordinate2D coordinate in coordinates)
            {
                if (!allowRepeated)
                {
                    if (lastIndex >= 0 && lastIndex == coordinate.Index)
                    {
                        lastIndex = coordinate.Index;
                        continue;
                    }

                    lastIndex = coordinate.Index;
                }

                addInternal(coordinate);
            }

            OnSequenceChanged();

            return this;
        }

        public IBufferedCoordSequence AddRange(IEnumerable<BufferedCoordinate2D> coordinates)
        {
            checkFrozen();

            foreach (BufferedCoordinate2D coordinate in coordinates)
            {
                addInternal(coordinate);
            }

            OnSequenceChanged();

            return this;
        }

        public IBufferedCoordSequence AddSequence(IBufferedCoordSequence sequence)
        {
            if (sequence == null) throw new ArgumentNullException("sequence");

            checkFrozen();

            _sequence.Capacity = Math.Max(_sequence.Capacity,
                                          sequence.Count - (_sequence.Capacity - Count));

            BufferedCoordinate2DSequence buf2DSeq = sequence as BufferedCoordinate2DSequence;

            // if we share a buffer, we can just import the indexes
            if (buf2DSeq != null && buf2DSeq._buffer == _buffer)
            {
                _sequence.AddRange(buf2DSeq._sequence);
            }
            else
            {
                foreach (BufferedCoordinate2D coordinate in sequence)
                {
                    addInternal(coordinate);
                }
            }

            OnSequenceChanged();
            return this;
        }

        public ISet<BufferedCoordinate2D> AsSet()
        {
            return new BufferedCoordinate2DSet(this, _factory, _buffer);
        }

        public IBufferedCoordSequence Clear()
        {
            _sequence.Clear();
            OnSequenceChanged();
            return this;
        }

        public IBufferedCoordSequence Clone()
        {
            BufferedCoordinate2DSequence clone
                = new BufferedCoordinate2DSequence(_factory, _buffer);

            clone._sequence.AddRange(_sequence);

            return clone;
        }

        public IBufferedCoordSequence CloseRing()
        {
            checkFrozen();

            if (Count < 3)
            {
                throw new InvalidOperationException(
                    "The coordinate sequence has less than 3 points, " +
                    "and cannot be a ring.");
            }

            if (!First.Equals(Last))
            {
                Add(First);
                OnSequenceChanged();
            }

            return this;
        }

        public Boolean Contains(BufferedCoordinate2D item)
        {
            Int32 coordIndex = item.Index;
            return _sequence.Contains(coordIndex);
        }

        public Int32 CompareTo(IBufferedCoordSequence other)
        {
            Int32 size1 = Count;
            Int32 size2 = other.Count;

            Int32 dim1 = (Int32)Dimension;
            Int32 dim2 = (Int32)other.Dimension;

            // lower dimension is less than higher
            if (dim1 < dim2) return -1;
            if (dim1 > dim2) return 1;

            // lexicographic ordering of point sequences
            Int32 i = 0;

            while (i < size1 && i < size2)
            {
                Int32 ptComp = this[i].CompareTo(other[i]);

                if (ptComp != 0)
                {
                    return ptComp;
                }

                i++;
            }

            if (i < size1) return 1;
            if (i < size2) return -1;

            return 0;
        }

        public IBufferedCoordFactory CoordinateFactory
        {
            get { return _factory.CoordinateFactory; }
        }

        public void CopyTo(BufferedCoordinate2D[] array, Int32 arrayIndex)
        {
            checkCopyToParameters(array, arrayIndex, "arrayIndex");

            for (Int32 index = 0; index < Count; index++)
            {
                array[index + arrayIndex] = this[index];
            }
        }

        public Int32 Count
        {
            get
            {
                return isSlice()
                           ? computeSliceCount()
                           : _sequence.Count;
            }
        }

        public CoordinateDimensions Dimension
        {
            get { return CoordinateDimensions.Two; }
        }

        public Boolean Equals(IBufferedCoordSequence other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            if (Count != other.Count)
            {
                return false;
            }

            BufferedCoordinate2DSequence buf2DSeq
                = other as BufferedCoordinate2DSequence;

            Int32 count = Count;

            if (ReferenceEquals(buf2DSeq, null))
            {
                for (Int32 index = 0; index < count; index++)
                {
                    if (!this[index].Equals(other[index]))
                    {
                        return false;
                    }
                }
            }
            else
            {
                for (Int32 index = 0; index < count; index++)
                {
                    if (_sequence[index] != buf2DSeq._sequence[index])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public IExtents<BufferedCoordinate2D> ExpandExtents(
                                                 IExtents<BufferedCoordinate2D> extents)
        {
            IExtents<BufferedCoordinate2D> expanded = extents;
            expanded.ExpandToInclude(this);
            return expanded;
        }

        public BufferedCoordinate2D First
        {
            get { return Count > 0 ? this[0] : new BufferedCoordinate2D(); }
        }

        public IBufferedCoordSequence Freeze()
        {
            _isFrozen = true;
            return this;
        }

        public IEnumerator<BufferedCoordinate2D> GetEnumerator()
        {
            // prepended indexes are stored in reverse, to allow more 
            // efficient addition
            if (_prependedIndexes != null)
            {
                for (Int32 i = _prependedIndexes.Count; i >= 0; i--)
                {
                    yield return _buffer[_prependedIndexes[i]];
                }
            }

            Int32 start = computeSliceEndOnMainSequence();
            Int32 end = computeSliceEndOnMainSequence();

            if (_skipIndexes != null)
            {
                for (Int32 i = start; i < end; i++)
                {
                    Int32 index = _sequence[i];

                    if (!_skipIndexes.Contains(index))
                    {
                        yield return _buffer[index];
                    }
                }
            }
            else
            {
                for (Int32 i = start; i < end; i++)
                {
                    Int32 index = _sequence[i];
                    yield return _buffer[index];
                }
            }

            if (_appendedIndexes != null)
            {
                foreach (Int32 index in _appendedIndexes)
                {
                    yield return _buffer[index];
                }
            }
        }

        public Boolean HasRepeatedCoordinates
        {
            get
            {
                Int32 lastValue = -1;

                for (int index = 0; index < Count; index++)
                {
                    Int32 currentValue = _sequence[index];

                    if (lastValue == currentValue)
                    {
                        return true;
                    }

                    lastValue = currentValue;
                }

                return false;
            }
        }

        public Int32 IncreasingDirection
        {
            get
            {
                Int32 midPoint = Count / 2;

                for (Int32 index = 0; index < midPoint; index++)
                {
                    Int32 j = Count - 1 - index;

                    // reflecting about midpoint, compare the coordinates
                    Int32 comp = this[j].CompareTo(this[index]);

                    if (comp != 0)
                    {
                        return comp;
                    }
                }

                // must be a palindrome - defined to be in positive direction
                return 1;
            }
        }

        public Int32 IndexOf(BufferedCoordinate2D item)
        {
            Int32 coordIndex = item.Index;
            Int32 index;

            List<Int32> firstList;
            SequenceStorage storage;

            if(_reversed)
            {
                firstList = _appendedIndexes;
                storage = SequenceStorage.AppendList;
            }
            else
            {
                firstList = _prependedIndexes;
                storage = SequenceStorage.PrependList;
            }

            if(firstList != null)
            {
                index = firstList.IndexOf(coordIndex);

                if(index >= 0)
                {
                    return inverseTransformIndex(index, storage);
                }
            }

            Int32 start = computeSliceStartOnMainSequence();
            index = _sequence.IndexOf(coordIndex, start, Count);

            if (index >= 0)
            {
                return inverseTransformIndex(index, SequenceStorage.MainList);
            }

            List<Int32> lastList;

            if (_reversed)
            {
                lastList = _prependedIndexes;
                storage = SequenceStorage.PrependList;
            }
            else
            {
                lastList = _appendedIndexes;
                storage = SequenceStorage.AppendList;
            }

            index = lastList.IndexOf(coordIndex);


            if (index >= 0)
            {
                return inverseTransformIndex(index, storage);
            }

            return -1;
        }

        public IBufferedCoordSequence Insert(Int32 index, BufferedCoordinate2D item)
        {
            checkFrozen();

            Boolean isSlice = this.isSlice();

            if (isSlice && index > 0 && index <= LastIndex)
            {
                throw new NotSupportedException(
                    "Inserting into a sliced coordinate sequence not supported. " +
                    "Use the coordinate sequence factory to create a new sequence " +
                    "from this slice in order to insert coordinates at this index.");
            }

            if (index < 0 || index > Count)
            {
                throw new ArgumentOutOfRangeException("index", index,
                                                      "Index must be between 0 and Count.");
            }
            index = _reversed ? Count - index : index;

            if (isSlice)
            {
                if (index == 0)
                {
                    Prepend(item);
                }
                else
                {
                    Append(item);
                }
            }
            else
            {
                _sequence.Insert(index, item.Index);
            }

            OnSequenceChanged();
            return this;
        }

        public BufferedCoordinate2D this[Int32 index]
        {
            get
            {
                SequenceStorage storage = checkAndTransformIndex(index,
                                                                 "index",
                                                                 out index);
                Int32 bufferIndex = -1;

                switch (storage)
                {
                    case SequenceStorage.AppendList:
                        bufferIndex = _appendedIndexes[index];
                        break;
                    case SequenceStorage.MainList:
                        bufferIndex = _sequence[index];
                        break;
                    case SequenceStorage.PrependList:
                        bufferIndex = _prependedIndexes[index];
                        break;
                    default:
                        Debug.Fail("Should never reach here.");
                        break;
                }

                return bufferIndex < 0
                    ? new BufferedCoordinate2D()
                    : _buffer[bufferIndex];
            }
            set
            {
                checkFrozen();

                SequenceStorage storage = checkAndTransformIndex(index,
                                                                 "index",
                                                                 out index);
                List<Int32> list = null;

                switch (storage)
                {
                    case SequenceStorage.AppendList:
                        list = _appendedIndexes;
                        break;
                    case SequenceStorage.MainList:
                        list = _sequence;
                        break;
                    case SequenceStorage.PrependList:
                        list = _prependedIndexes;
                        break;
                    default:
                        Debug.Fail("Should never reach here.");
                        break;
                }


                // TODO: I can't figure out a test to prove the defect in the 
                // following commented-out line...

                //_sequence[index] = _buffer.Add(value);

                Debug.Assert(list != null);
                list[index] = _factory.CoordinateFactory.Create(value).Index;

                OnSequenceChanged();
            }
        }

        public Double this[Int32 index, Ordinates ordinate]
        {
            get
            {
                checkOrdinate(ordinate);

                return this[index][ordinate];
            }
            set
            {
                checkFrozen();
                checkOrdinate(ordinate);

                throw new NotImplementedException();
                //onSequenceChanged();
            }
        }

        public Boolean IsFixedSize
        {
            get { return IsFrozen; }
        }

        public Boolean IsFrozen
        {
            get { return _isFrozen; }
        }

        public Boolean IsReadOnly
        {
            get { return IsFrozen; }
        }

        public BufferedCoordinate2D Last
        {
            get { return Count > 0 ? this[Count - 1] : new BufferedCoordinate2D(); }
        }

        public Int32 LastIndex
        {
            get { return Count - 1; }
        }

        public BufferedCoordinate2D Maximum
        {
            get
            {
                if (_max < 0)
                {
                    Int32 maxIndex = -1;
                    BufferedCoordinate2D maxCoord = new BufferedCoordinate2D();

                    if (Count < 1)
                    {
                        return maxCoord;
                    }


                    for (int i = 0; i < Count; i++)
                    {
                        BufferedCoordinate2D current = this[i];

                        if (maxCoord.IsEmpty || current.GreaterThan(maxCoord))
                        {
                            maxIndex = i;
                            maxCoord = current;
                        }
                    }

                    _max = maxIndex;
                }

                return this[_max];
            }
        }

        public IBufferedCoordSequence Merge(IBufferedCoordSequence other)
        {
            throw new NotImplementedException();
        }

        public BufferedCoordinate2D Minimum
        {
            get
            {
                if (Count < 1)
                {
                    return new BufferedCoordinate2D();
                }

                if (_min < 0)
                {
                    Int32 minIndex = -1;
                    BufferedCoordinate2D? minCoord = null;

                    for (int i = 0; i < Count; i++)
                    {
                        BufferedCoordinate2D current = this[i];

                        if (minCoord == null || current.LessThan(minCoord.Value))
                        {
                            minIndex = i;
                            minCoord = current;
                        }
                    }

                    _min = minIndex;
                }

                return this[_min];
            }
        }

        public IBufferedCoordSequence Prepend(BufferedCoordinate2D coordinate)
        {
            if (coordinate.IsEmpty)
            {
                throw new ArgumentException("Coordinate cannot be empty.");
            }

            Int32 coordIndex = coordinate.Index;
            prependCoordIndex(coordIndex);
            return this;
        }

        public IBufferedCoordSequence Prepend(IEnumerable<BufferedCoordinate2D> coordinates)
        {
            BufferedCoordinate2DSequence seq = coordinates as BufferedCoordinate2DSequence;

            if (seq != null)
            {
                prependInternal(seq);
            }
            else
            {
                foreach (BufferedCoordinate2D coordinate in coordinates)
                {
                    Insert(0, coordinate);
                }
            }

            return this;
        }

        public IBufferedCoordSequence Prepend(IBufferedCoordSequence coordinates)
        {
            BufferedCoordinate2DSequence seq = coordinates as BufferedCoordinate2DSequence;

            if (seq != null)
            {
                prependInternal(seq);
                return this;
            }

            return Prepend((IEnumerable<BufferedCoordinate2D>)coordinates);
        }

        public Boolean Remove(BufferedCoordinate2D item)
        {
            checkFrozen();
            Boolean result = _sequence.Remove(item.Index);
            OnSequenceChanged();
            return result;
        }

        public IBufferedCoordSequence RemoveAt(Int32 index)
        {
            checkFrozen();
            SequenceStorage storage = checkAndTransformIndex(index, "index", out index);

            if (isSlice())
            {
                skipIndex(index);
                return this;
            }

            if (storage == SequenceStorage.MainList)
            {
                skipIndex(index);
            }
            else
            {
                switch (storage)
                {
                    case SequenceStorage.AppendList:
                        _appendedIndexes.RemoveAt(index);
                        break;
                    case SequenceStorage.PrependList:
                        _prependedIndexes.RemoveAt(index);
                        break;
                    default:
                        break;
                }
            }

            OnSequenceChanged();
            return this;
        }

        /// <summary>
        /// Reverses the coordinates in a sequence in-place.
        /// </summary>
        public IBufferedCoordSequence Reverse()
        {
            checkFrozen();

            _reversed = !_reversed;

            OnSequenceChanged();

            return this;
        }

        public IBufferedCoordSequence Reversed
        {
            get
            {
                IBufferedCoordSequence reversed = Clone();
                reversed.Reverse();
                return reversed;
            }
        }

        public IBufferedCoordSequence Scroll(BufferedCoordinate2D coordinateToBecomeFirst)
        {
            checkFrozen();
            OnSequenceChanged();
            throw new NotImplementedException();
        }

        public IBufferedCoordSequence Scroll(Int32 indexToBecomeFirst)
        {
            checkFrozen();
            OnSequenceChanged();
            throw new NotImplementedException();
        }

        public IBufferedCoordSequence Slice(Int32 startIndex, Int32 endIndex)
        {
            checkIndexes(endIndex, startIndex);
            Freeze();

            if (!isSlice())
            {
                return new BufferedCoordinate2DSequence(_sequence,
                                                        startIndex, endIndex,
                                                        _factory, _buffer);
            }
            if (_prependedIndexes != null || _appendedIndexes != null || _skipIndexes != null)
            {
                throw new NotImplementedException("Slice of a slice containing prepended, appended, or skipped indices not implemented");
            }
            return new BufferedCoordinate2DSequence(_sequence,
                                                    startIndex + Math.Max(0, _startIndex), endIndex + Math.Max(0, _startIndex),
                                                    _factory, _buffer);
        }

        public IBufferedCoordSequence Sort()
        {
            Sort(0, LastIndex, _factory.DefaultComparer);
            return this;
        }

        public IBufferedCoordSequence Sort(Int32 startIndex, Int32 endIndex)
        {
            Sort(startIndex, endIndex, _factory.DefaultComparer);
            return this;
        }

        public IBufferedCoordSequence Sort(Int32 startIndex, Int32 endIndex, IComparer<BufferedCoordinate2D> comparer)
        {
            checkFrozen();

            checkIndexes(endIndex, startIndex);

            if (startIndex == endIndex)
            {
                return this;
            }

            List<BufferedCoordinate2D> coords = new List<BufferedCoordinate2D>(endIndex - startIndex);

            for (Int32 i = startIndex; i <= endIndex; i++)
            {
                coords.Add(this[i]);
            }

            coords.Sort(comparer);

            for (Int32 i = startIndex; i <= endIndex; i++)
            {
                this[i] = coords[i];
            }

            OnSequenceChanged();
            return this;
        }

        public IBufferedCoordSequence Splice(IEnumerable<BufferedCoordinate2D> coordinates,
                                             Int32 startIndex,
                                             Int32 endIndex)
        {
            BufferedCoordinate2DSequence seq = createSliceInternal(endIndex, startIndex);

            seq.Prepend(coordinates);

            return seq;
        }

        public IBufferedCoordSequence Splice(BufferedCoordinate2D coordinate,
                                             Int32 startIndex,
                                             Int32 endIndex)
        {
            BufferedCoordinate2DSequence seq = createSliceInternal(endIndex, startIndex);

            seq.Prepend(coordinate);

            return seq;
        }

        public IBufferedCoordSequence Splice(IEnumerable<BufferedCoordinate2D> startCoordinates,
                                             Int32 startIndex,
                                             Int32 endIndex,
                                             BufferedCoordinate2D endCoordinate)
        {
            BufferedCoordinate2DSequence seq = createSliceInternal(endIndex, startIndex);

            return seq.Prepend(startCoordinates).Append(endCoordinate);
        }

        public IBufferedCoordSequence Splice(BufferedCoordinate2D startCoordinate,
                                             Int32 startIndex,
                                             Int32 endIndex,
                                             BufferedCoordinate2D endCoordinate)
        {
            BufferedCoordinate2DSequence seq = createSliceInternal(endIndex, startIndex);

            return seq.Prepend(startCoordinate).Append(endCoordinate);
        }

        public IBufferedCoordSequence Splice(IEnumerable<BufferedCoordinate2D> startCoordinates,
                                             Int32 startIndex,
                                             Int32 endIndex,
                                             IEnumerable<BufferedCoordinate2D> endCoordinates)
        {
            BufferedCoordinate2DSequence seq = createSliceInternal(endIndex, startIndex);

            return seq.Prepend(startCoordinates).Append(endCoordinates);
        }

        public IBufferedCoordSequence Splice(BufferedCoordinate2D startCoordinate,
                                             Int32 startIndex,
                                             Int32 endIndex,
                                             IEnumerable<BufferedCoordinate2D> endCoordinates)
        {
            BufferedCoordinate2DSequence seq = createSliceInternal(endIndex, startIndex);

            return seq.Prepend(startCoordinate).Append(endCoordinates);
        }

        public IBufferedCoordSequence Splice(Int32 startIndex,
                                             Int32 endIndex,
                                             IEnumerable<BufferedCoordinate2D> coordinates)
        {
            BufferedCoordinate2DSequence seq = createSliceInternal(endIndex, startIndex);

            return seq.Append(coordinates);
        }

        public IBufferedCoordSequence Splice(Int32 startIndex,
                                             Int32 endIndex,
                                             BufferedCoordinate2D coordinate)
        {
            BufferedCoordinate2DSequence seq = createSliceInternal(endIndex, startIndex);

            return seq.Append(coordinate);
        }

        public IBufferedCoordSequence WithoutDuplicatePoints()
        {
            Int32[] indexes = _sequence.ToArray();
            Array.Sort(indexes, 0, indexes.Length);
            List<Int32> duplicates = new List<Int32>(32);

            Int32 lastIndex = -1;

            foreach (Int32 index in indexes)
            {
                if (index == lastIndex)
                {
                    duplicates.Add(index);
                }

                lastIndex = index;
            }

            LinkedList<Int32> coordsToFix = new LinkedList<Int32>(_sequence);

            foreach (Int32 duplicate in duplicates)
            {
                coordsToFix.Remove(duplicate);
            }

            BufferedCoordinate2DSequence noDupes = new BufferedCoordinate2DSequence(_factory, _buffer);

            noDupes._sequence.AddRange(coordsToFix);

            return noDupes;
        }

        public IBufferedCoordSequence WithoutRepeatedPoints()
        {
            return _factory.Create(this, false, true);
        }

        public event EventHandler SequenceChanged;

        #endregion

        IExtents ICoordinateSequence.ExpandExtents(IExtents extents)
        {
            IExtents expanded = extents;
            expanded.ExpandToInclude(this);
            return expanded;
        }

        ICoordinateSequence ICoordinateSequence.Merge(ICoordinateSequence other)
        {
            throw new NotImplementedException();
        }

        ICoordinate[] ICoordinateSequence.ToArray()
        {
            throw new NotImplementedException();
        }

        ICoordinate ICoordinateSequence.First
        {
            get { return First; }
        }

        ICoordinate ICoordinateSequence.Last
        {
            get { return Last; }
        }

        ICoordinate ICoordinateSequence.this[Int32 index]
        {
            get { throw new NotImplementedException(); }
            set
            {
                checkFrozen();
                throw new NotImplementedException();
                OnSequenceChanged();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        Object IList.this[Int32 index]
        {
            get { return this[index]; }
            set
            {
                checkFrozen();
                throw new NotImplementedException();
                OnSequenceChanged();
            }
        }

        Int32 IList.Add(Object value)
        {
            checkFrozen();

            if (value is BufferedCoordinate2D)
            {
                BufferedCoordinate2D coord = (BufferedCoordinate2D)value;
                Add(coord); // OnSequenceChanged() called here
                return _sequence.Count - 1;
            }

            throw new ArgumentException("Parameter must be a BufferedCoordinate2D.");
        }

        void IList.Remove(Object value)
        {
            checkFrozen();
            throw new NotImplementedException();
            OnSequenceChanged();
        }

        Boolean IList.Contains(Object value)
        {
            if (!(value is BufferedCoordinate2D))
            {
                return false;
            }

            return Contains((BufferedCoordinate2D)value);
        }

        Int32 IList.IndexOf(Object value)
        {
            if (!(value is BufferedCoordinate2D))
            {
                return -1;
            }

            return IndexOf((BufferedCoordinate2D)value);
        }

        void IList.Insert(Int32 index, Object value)
        {
            if (value == null) throw new ArgumentNullException("value");
            //checkFrozen();
            //index = checkAndTransformIndex(index, "index");
            ICoordinate coord = value as ICoordinate;

            if (coord == null)
            {
                throw new ArgumentException("value must be an ICoordinate instance.");
            }

            Insert(index, _factory.CoordinateFactory.Create(coord));
        }

        void ICollection.CopyTo(Array array, Int32 index)
        {
            checkCopyToParameters(array, index, "index");

            for (int i = 0; i < Count; i++)
            {
                array.SetValue(this[i], i + index);
            }
        }

        Boolean ICollection.IsSynchronized
        {
            get { return false; }
        }

        Object ICollection.SyncRoot
        {
            get { throw new NotSupportedException(); }
        }

        ICoordinateSequence ICoordinateSequence.Clone()
        {
            return Clone();
        }

        Object ICloneable.Clone()
        {
            return Clone();
        }

        #region IList<BufferedCoordinate2D> Members


        void IList<BufferedCoordinate2D>.Insert(Int32 index, BufferedCoordinate2D item)
        {
            Insert(index, item);
        }

        void IList<BufferedCoordinate2D>.RemoveAt(Int32 index)
        {
            RemoveAt(index);
        }

        #endregion

        #region ICoordinateSequence Members

        ICoordinateSequence ICoordinateSequence.Freeze()
        {
            return Freeze();
        }

        ICoordinate ICoordinateSequence.Maximum
        {
            get { return Maximum; }
        }

        ICoordinate ICoordinateSequence.Minimum
        {
            get { return Minimum; }
        }

        ICoordinateSequence ICoordinateSequence.Reverse()
        {
            return Reverse();
        }

        ICoordinateSequence ICoordinateSequence.Reversed
        {
            get { return Reversed; }
        }

        #endregion

        #region ICollection<BufferedCoordinate2D> Members


        void ICollection<BufferedCoordinate2D>.Clear()
        {
            Clear();
        }

        #endregion

        #region IList Members


        void IList.Clear()
        {
            Clear();
        }

        void IList.RemoveAt(Int32 index)
        {
            RemoveAt(index);
        }

        #endregion

        protected void OnSequenceChanged()
        {
            EventHandler e = SequenceChanged;

            if (e != null)
            {
                e(this, EventArgs.Empty);
            }

            _min = -1;
            _max = -1;
        }

        protected void SetSequenceInternal(BufferedCoordinate2DSequence sequence)
        {
            if (sequence.IsFrozen)
            {
                _sequence = sequence._sequence;
            }
            else
            {
                _sequence.Clear();
                _sequence.AddRange(sequence._sequence);
            }

            _startIndex = sequence._startIndex;
            _endIndex = sequence._endIndex;
            _reversed = sequence._reversed;
            _isFrozen = sequence._isFrozen;
            _appendedIndexes = sequence._appendedIndexes == null
                                        ? null
                                        : new List<Int32>(sequence._appendedIndexes);
            _prependedIndexes = sequence._prependedIndexes == null
                                        ? null
                                        : new List<Int32>(sequence._prependedIndexes);
            _skipIndexes = sequence._skipIndexes == null
                                        ? null
                                        : new SortedSet<Int32>(sequence._skipIndexes);
            _min = sequence._min;
            _max = sequence._max;
        }

        //private void swap(Int32 i, Int32 j)
        //{
        //    //checkIndex(i, "i");
        //    //checkIndex(j, "j");

        //    if (i == j)
        //    {
        //        return;
        //    }

        //    Int32 temp = _sequence[i];
        //    _sequence[i] = _sequence[j];
        //    _sequence[j] = temp;
        //}

        private SequenceStorage checkAndTransformIndex(Int32 index, String parameterName, out Int32 transformedIndex)
        {
            checkIndex(index, parameterName);
            return transformIndex(index, out transformedIndex);
        }

        private void checkIndex(Int32 index, String parameterName)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(parameterName, index,
                                                      "Index must be between 0 and " +
                                                      "Count - 1.");
            }
        }

        private Int32 inverseTransformIndex(Int32 index, SequenceStorage storage)
        {
            index = index - Math.Max(0, _startIndex);
            return _reversed ? (Count - 1) - index : index;
        }

        private SequenceStorage transformIndex(Int32 index, out Int32 transformedIndex)
        {
            // First, project index on reversed sequence, if needed
            index = _reversed ? (Count - 1) - index : index;

            // Get the count of the indexes before the main sequence
            // If reversed, this means the appended indexes
            Int32 prependCount = _reversed
                           ? _appendedIndexes == null
                                 ? 0
                                 : _appendedIndexes.Count
                           : _prependedIndexes == null
                                 ? 0
                                 : _prependedIndexes.Count;

            SequenceStorage storage;

            // If the index is smaller than the prepended count, 
            // index into the prepended storage
            if (index < prependCount)
            {
                storage = _reversed
                    ? SequenceStorage.PrependList
                    : SequenceStorage.AppendList;
                transformedIndex = index;
            }
            else
            {
                Int32 endIndex = computeSliceEndOnMainSequence();
                Int32 startIndex = computeSliceStartOnMainSequence();
                Int32 mainSequenceCount = (endIndex - startIndex) + 1 -
                                          (_skipIndexes == null ? 0 : _skipIndexes.Count);
                Int32 firstAppendIndex = prependCount + mainSequenceCount;

                // If the index is greater or equal to the sum of both
                // prepended and main sequence slices, then it must index
                // into the appended list
                if (index >= firstAppendIndex)
                {
                    storage = _reversed
                        ? SequenceStorage.AppendList
                        : SequenceStorage.PrependList;
                    transformedIndex = index - firstAppendIndex;
                }
                else
                {
                    storage = SequenceStorage.MainList;
                    Int32 mainIndex = index - prependCount + startIndex;
                    Int32 skips = _skipIndexes == null 
                                        ? 0 
                                        : _reversed
                                              ? _skipIndexes.CountBefore(mainIndex)
                                              : _skipIndexes.CountAfter(mainIndex);
                    transformedIndex = mainIndex + skips;
                }
            }

            return storage;
        }

        private void checkOrdinate(Ordinates ordinate)
        {
            if (ordinate == Ordinates.Z || ordinate == Ordinates.M)
            {
                throw new ArgumentOutOfRangeException("ordinate", ordinate,
                                                      "The ICoordinateSequence does " +
                                                      "not have this dimension");
            }
        }

        private void checkFrozen()
        {
            if (_isFrozen)
            {
                throw new InvalidOperationException(
                    "Sequence is frozen and cannot be modified.");
            }
        }

        private void checkIndexes(Int32 endIndex, Int32 startIndex)
        {
            if (endIndex < startIndex)
            {
                throw new ArgumentException(
                    "startIndex must be less than or equal to endIndex.");
            }

            checkIndex(startIndex, "startIndex");
            checkIndex(endIndex, "endIndex");
        }

        private void checkCopyToParameters(Array array, Int32 arrayIndex, String arrayIndexName)
        {
            if (array == null) throw new ArgumentNullException("array");

            if (arrayIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    arrayIndexName, arrayIndex, "Index cannot be less than 0");
            }

            if (arrayIndex >= array.Length)
            {
                throw new ArgumentException(
                    "Index is greater than or equal to length of array");
            }

            if (array.Length - arrayIndex < Count)
            {
                throw new ArgumentException(String.Format(
                    "The number of elements to copy is greater than the " +
                    "remaining space of 'array' when starting at '{0}'.", arrayIndexName));
            }
        }

        private Boolean isSlice()
        {
            return _startIndex >= 0 || _endIndex >= 0;
        }

        private void addInternal(BufferedCoordinate2D item)
        {
            if (!ReferenceEquals(item.Factory, _factory.CoordinateFactory))
            {
                item = _factory.CoordinateFactory.Create(item);
            }

            Int32 index = item.Index;

            if (isSlice())
            {
                appendCoordIndex(index);
            }
            else
            {
                _sequence.Add(index);
            }
        }

        private void appendCoordIndex(Int32 coordIndex)
        {
            // if we are already appending indexes, put it in the 
            // appropriate appending list... which means the prepended list 
            // for reverse sequences
            if (_reversed && _prependedIndexes != null)
            {
                _prependedIndexes.Add(coordIndex);
                return;
            }

            if (_appendedIndexes != null)
            {
                _appendedIndexes.Add(coordIndex);
                return;
            }

            // not a slice, treat the whole sequence
            if (!isSlice())
            {
                if (_reversed)
                {
                    // if we are appending to a reversed sequence, we
                    // really want to prepend... however this would
                    // translate into an insert (read: copy), which we are currently
                    // avoiding. Thus, add to the prepend list. 
                    // TODO: consider a heuristic to judge when an insert
                    // to the head of a list would be better than a multiple list
                    // sequence.
                    Debug.Assert(_prependedIndexes == null);
                    _prependedIndexes = new List<Int32>();
                    _prependedIndexes.Add(coordIndex);
                }
                else
                {
                    Debug.Assert(_appendedIndexes == null);
                    _sequence.Add(coordIndex);
                }

                return;
            }

            // project index to slice
            Int32 transformedIndex;
            transformIndex(LastIndex, out transformedIndex);

            // if the next coord in the sequence is the same, just adjust
            // the end of the slice
            if (_reversed)
            {
                if (transformedIndex > 0 &&
                    _startIndex >= 0 &&
                    _sequence[transformedIndex - 1] == coordIndex)
                {
                    _startIndex--;
                }
                else
                {
                    _prependedIndexes = new List<Int32>();
                    _prependedIndexes.Add(coordIndex);
                }
            }
            else
            {
                if (transformedIndex < _sequence.Count - 1 &&
                    _endIndex >= 0 &&
                    _sequence[transformedIndex + 1] == coordIndex)
                {
                    _endIndex++;
                }
                else
                {
                    _appendedIndexes = new List<Int32>();
                    _appendedIndexes.Add(coordIndex);
                }
            }
        }

        private void prependCoordIndex(Int32 coordIndex)
        {
            // if we are already prepending indexes, put it in the 
            // appropriate prepending list... which means the appended list 
            // for reverse sequences
            if (_reversed && _appendedIndexes != null)
            {
                _appendedIndexes.Add(coordIndex);
                return;
            }

            if (_prependedIndexes != null)
            {
                _prependedIndexes.Add(coordIndex);
                return;
            }

            // not a slice, treat the whole sequence
            if (!isSlice())
            {
                if (_reversed)
                {
                    // if we are prepending to a reversed sequence, we
                    // really want to append, so just add it.
                    Debug.Assert(_appendedIndexes == null);
                    _sequence.Add(coordIndex);
                }
                else
                {
                    // We want to avoid copying the entire sequence in memory just to 
                    // insert a coordinate.
                    // Thus, add to the prepend list. 
                    // TODO: consider a heuristic to judge when an insert
                    // to the head of a list would be better than a multiple list
                    // sequence.
                    Debug.Assert(_prependedIndexes == null);
                    _prependedIndexes = new List<Int32>();
                    _prependedIndexes.Add(coordIndex);
                }

                return;
            }

            // project index to slice
            Int32 transformedIndex;
            transformIndex(0, out transformedIndex);

            // if the next coord in the sequence is the same, just adjust
            // the end of the slice
            if (_reversed)
            {
                if (transformedIndex < _sequence.Count - 1 &&
                    _endIndex >= 0 &&
                    _sequence[transformedIndex + 1] == coordIndex)
                {
                    _endIndex++;
                }
                else
                {
                    _appendedIndexes = new List<Int32>();
                    _appendedIndexes.Add(coordIndex);
                }
            }
            else
            {
                if (transformedIndex > 0 &&
                    _startIndex >= 0 &&
                    _sequence[transformedIndex - 1] == coordIndex)
                {
                    _startIndex--;
                }
                else
                {
                    _prependedIndexes = new List<Int32>();
                    _prependedIndexes.Add(coordIndex);
                }
            }
        }

        private int computeSliceCount()
        {
            Int32 end = computeSliceEndOnMainSequence();
            Int32 start = computeSliceStartOnMainSequence();

            return end - start + 1 +
                   (_appendedIndexes == null ? 0 : _appendedIndexes.Count) +
                   (_prependedIndexes == null ? 0 : _prependedIndexes.Count) -
                   (_skipIndexes == null ? 0 : _skipIndexes.Count);
        }

        private void skipIndex(Int32 index)
        {
            if (_skipIndexes == null)
            {
                _skipIndexes = new SortedSet<Int32>();
            }

            _skipIndexes.Add(index);
        }

        private void appendInternal(BufferedCoordinate2DSequence sequence)
        {
            if (!ReferenceEquals(sequence._buffer, _buffer))
            {
                Append((IEnumerable<BufferedCoordinate2D>)sequence);
            }

            if (_appendedIndexes == null)
            {
                _appendedIndexes = new List<Int32>(Math.Max(4, sequence.Count));
            }

            _appendedIndexes.AddRange(sequence._sequence);
        }

        private void prependInternal(BufferedCoordinate2DSequence sequence)
        {
            if (!ReferenceEquals(sequence._buffer, _buffer))
            {
                Prepend((IEnumerable<BufferedCoordinate2D>)sequence);
            }

            if (_prependedIndexes == null)
            {
                _prependedIndexes = new List<Int32>(Math.Max(4, sequence.Count));
            }

            _prependedIndexes.AddRange(sequence._sequence);
        }

        private BufferedCoordinate2DSequence createSliceInternal(Int32 endIndex, Int32 startIndex)
        {
            checkIndexes(endIndex, startIndex);

            Freeze();

            return new BufferedCoordinate2DSequence(_sequence,
                                                    startIndex, endIndex,
                                                    _factory, _buffer);
        }

        private Int32 computeSliceStartOnMainSequence()
        {
            return Math.Max(0, _startIndex);
        }

        private Int32 computeSliceEndOnMainSequence()
        {
            return (Int32)Math.Min(_sequence.Count - 1, (UInt32)_endIndex);
        }
    }
}