﻿using System;
using System.IO;
using System.Threading.Tasks;
using FlatFiles.Resources;

namespace FlatFiles
{
    /// <summary>
    /// Extracts records from a file that has value in fixed-length columns.
    /// </summary>
    public sealed class FixedLengthReader : IReader
    {
        private readonly FixedLengthRecordParser parser;
        private readonly Metadata metadata;
        private object[] values;
        private bool endOfFile;
        private bool hasError;

        /// <summary>
        /// Initializes a new FixedLengthReader with the given schema.
        /// </summary>
        /// <param name="reader">A reader over the fixed-length document.</param>
        /// <param name="schema">The schema of the fixed-length document.</param>
        /// <param name="options">The options controlling how the fixed-length document is read.</param>
        /// <exception cref="ArgumentNullException">The reader is null.</exception>
        /// <exception cref="ArgumentNullException">The schema is null.</exception>
        public FixedLengthReader(TextReader reader, FixedLengthSchema schema, FixedLengthOptions options = null)
        {
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }
            if (schema == null)
            {
                throw new ArgumentNullException("schema");
            }
            if (options == null)
            {
                options = new FixedLengthOptions();
            }
            parser = new FixedLengthRecordParser(reader, schema, options);
            this.metadata = new Metadata()
            {
                Schema = schema,
                Options = options.Clone()
            };
        }

        /// <summary>
        /// Gets the schema being used by the parser.
        /// </summary>
        /// <returns>The schema being used by the parser.</returns>
        public FixedLengthSchema GetSchema()
        {
            return metadata.Schema;
        } 

        ISchema IReader.GetSchema()
        {
            return GetSchema();
        }

        /// <summary>
        /// Gets the schema being used by the parser.
        /// </summary>
        /// <returns>The schema being used by the parser.</returns>
        public Task<FixedLengthSchema> GetSchemaAsync()
        {
            return Task.FromResult(metadata.Schema);
        }

        Task<ISchema> IReader.GetSchemaAsync()
        {
            return Task.FromResult<ISchema>(metadata.Schema);
        }

        /// <summary>
        /// Reads the next record from the file.
        /// </summary>
        /// <returns>True if the next record was parsed; otherwise, false if all files are read.</returns>
        public bool Read()
        {
            if (hasError)
            {
                throw new InvalidOperationException(SharedResources.ReadingWithErrors);
            }
            handleHeader();
            try
            {
                values = parsePartitions();
                if (values == null)
                {
                    return false;
                }
                else
                {
                    ++metadata.LogicalRecordCount;
                    return true;
                }
            }
            catch (FlatFileException)
            {
                hasError = true;
                throw;
            }
        }

        private void handleHeader()
        {
            if (metadata.RecordCount == 0 && metadata.Options.IsFirstRecordHeader)
            {
                skip();
            }
        }

        private object[] parsePartitions()
        {
            string[] rawValues = partitionWithFilter();
            while (rawValues != null)
            {
                object[] values = parseValues(rawValues);
                if (values != null)
                {
                    return values;
                }
                rawValues = partitionWithFilter();
            }
            return null;
        }

        private string[] partitionWithFilter()
        {
            string record = readWithFilter();
            string[] rawValues = partitionRecord(record);
            while (rawValues != null && metadata.Options.PartitionedRecordFilter != null && metadata.Options.PartitionedRecordFilter(rawValues))
            {
                record = readWithFilter();
                rawValues = partitionRecord(record);
            }
            return rawValues;
        }

        private string readWithFilter()
        {
            string record = readNextRecord();
            while (record != null && metadata.Options.UnpartitionedRecordFilter != null && metadata.Options.UnpartitionedRecordFilter(record))
            {
                record = readNextRecord();
            }
            return record;
        }

        /// <summary>
        /// Reads the next record from the file.
        /// </summary>
        /// <returns>True if the next record was parsed; otherwise, false if all files are read.</returns>
        public async ValueTask<bool> ReadAsync()
        {
            if (hasError)
            {
                throw new InvalidOperationException(SharedResources.ReadingWithErrors);
            }
            await handleHeaderAsync();
            try
            {
                values = await parsePartitionsAsync();
                if (values == null)
                {
                    return false;
                }
                else
                {
                    ++metadata.LogicalRecordCount;
                    return true;
                }
            }
            catch (FlatFileException)
            {
                hasError = true;
                throw;
            }
        }

        private async Task handleHeaderAsync()
        {
            if (metadata.RecordCount == 0 && metadata.Options.IsFirstRecordHeader)
            {
                await skipAsync();
            }
        }

        private async Task<object[]> parsePartitionsAsync()
        {
            string[] rawValues = await partitionWithFilterAsync();
            while (rawValues != null)
            {
                object[] values = parseValues(rawValues);
                if (values != null)
                {
                    return values;
                }
                rawValues = await partitionWithFilterAsync();
            }
            return null;
        }

        private async Task<string[]> partitionWithFilterAsync()
        {
            string record = await readWithFilterAsync();
            string[] rawValues = partitionRecord(record);
            while (rawValues != null && metadata.Options.PartitionedRecordFilter != null && metadata.Options.PartitionedRecordFilter(rawValues))
            {
                record = await readWithFilterAsync();
                rawValues = partitionRecord(record);
            }
            return rawValues;
        }

        private async Task<string> readWithFilterAsync()
        {
            string record = await readNextRecordAsync();
            while (record != null && metadata.Options.UnpartitionedRecordFilter != null && metadata.Options.UnpartitionedRecordFilter(record))
            {
                record = await readNextRecordAsync();
            }
            return record;
        }

        private object[] parseValues(string[] rawValues)
        {
            try
            {
                return metadata.Schema.ParseValues(metadata, rawValues);
            }
            catch (FlatFileException exception)
            {
                processError(new RecordProcessingException(metadata.RecordCount, SharedResources.InvalidRecordConversion, exception));
                return null;
            }
        }

        /// <summary>
        /// Skips the next record from the file.
        /// </summary>
        /// <returns>True if the next record was skipped; otherwise, false if all records are read.</returns>
        /// <remarks>The previously parsed values remain available.</remarks>
        public bool Skip()
        {
            if (hasError)
            {
                throw new InvalidOperationException(SharedResources.ReadingWithErrors);
            }
            handleHeader();
            return skip();
        }

        private bool skip()
        {
            string record = readNextRecord();
            return record != null;
        }

        /// <summary>
        /// Skips the next record from the file.
        /// </summary>
        /// <returns>True if the next record was skipped; otherwise, false if all records are read.</returns>
        /// <remarks>The previously parsed values remain available.</remarks>
        public async ValueTask<bool> SkipAsync()
        {
            if (hasError)
            {
                throw new InvalidOperationException(SharedResources.ReadingWithErrors);
            }
            await handleHeaderAsync();
            return await skipAsync();
        }

        private async ValueTask<bool> skipAsync()
        {
            string record = await readNextRecordAsync();
            return record != null;
        }

        private string[] partitionRecord(string record)
        {
            if (record == null)
            {
                return null;
            }
            if (record.Length < metadata.Schema.TotalWidth)
            {
                processError(new RecordProcessingException(metadata.RecordCount, SharedResources.FixedLengthRecordTooShort));
                return null;
            }
            WindowCollection windows = metadata.Schema.Windows;
            string[] values = new string[windows.Count - metadata.Schema.ColumnDefinitions.MetadataCount];
            int offset = 0;
            for (int index = 0; index != values.Length;)
            {
                var definition = metadata.Schema.ColumnDefinitions[index];
                if (!(definition is IMetadataColumn metaColumn))
                {
                    Window window = windows[index];
                    string value = record.Substring(offset, window.Width);
                    var alignment = window.Alignment ?? metadata.Options.Alignment;
                    if (alignment == FixedAlignment.LeftAligned)
                    {
                        value = value.TrimEnd(window.FillCharacter ?? metadata.Options.FillCharacter);
                    }
                    else
                    {
                        value = value.TrimStart(window.FillCharacter ?? metadata.Options.FillCharacter);
                    }
                    values[index] = value;
                    ++index;
                    offset += window.Width;
                }
            }
            return values;
        }

        private string readNextRecord()
        {
            if (parser.IsEndOfStream())
            {
                endOfFile = true;
                return null;
            }
            string record = parser.ReadRecord();
            ++metadata.RecordCount;
            return record;
        }

        private async Task<string> readNextRecordAsync()
        {
            if (await parser.IsEndOfStreamAsync())
            {
                endOfFile = true;
                return null;
            }
            string record = await parser.ReadRecordAsync();
            ++metadata.RecordCount;
            return record;
        }

        private void processError(RecordProcessingException exception)
        {
            if (metadata.Options.ErrorHandler != null)
            {
                var args = new ProcessingErrorEventArgs(exception);
                metadata.Options.ErrorHandler(this, args);
                if (args.IsHandled)
                {
                    return;
                }
            }
            throw exception;
        }

        /// <summary>
        /// Gets the values for the current record.
        /// </summary>
        /// <returns>The values of the current record.</returns>
        public object[] GetValues()
        {
            if (hasError)
            {
                throw new InvalidOperationException(SharedResources.ReadingWithErrors);
            }
            if (metadata.RecordCount == 0)
            {
                throw new InvalidOperationException(SharedResources.ReadNotCalled);
            }
            if (endOfFile)
            {
                throw new InvalidOperationException(SharedResources.NoMoreRecords);
            }
            object[] copy = new object[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }

        private class Metadata : IProcessMetadata
        {
            public FixedLengthSchema Schema { get; internal set; }

            ISchema IProcessMetadata.Schema
            {
                get { return Schema; }
            }

            public FixedLengthOptions Options { get; internal set; }

            IOptions IProcessMetadata.Options
            {
                get { return Options; }
            }

            public int RecordCount { get; internal set; }

            public int LogicalRecordCount { get; internal set; }
        }
    }
}
