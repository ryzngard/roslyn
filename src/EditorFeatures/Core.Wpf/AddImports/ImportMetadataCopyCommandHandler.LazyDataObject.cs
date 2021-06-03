// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Windows;

namespace Microsoft.CodeAnalysis.Editor.Implementation.AddImports
{
    internal sealed partial class ImportMetadataCopyCommandHandler
    {
        private sealed class LazyDataObject : IDataObject
        {
            private readonly IDataObject _wrappedObject;
            private readonly AddImportsCacheIdentifier _token;
            private readonly IAddImportsCopyCacheService _addImportsCopyCacheService;

            public LazyDataObject(IDataObject dataObject, AddImportsCacheIdentifier token, IAddImportsCopyCacheService addImportsCopyCacheService)
            {
                _wrappedObject = dataObject;
                _token = token;
                _addImportsCopyCacheService = addImportsCopyCacheService;
            }

            public object? GetData(string format)
            {
                if (format == ClipboardDataFormat)
                {
                    var task = _addImportsCopyCacheService
                        .GetDataAsync(_token);

                    task.Wait(TimeSpan.FromSeconds(2));

                    return task.Result;
                }

                return _wrappedObject.GetData(format);
            }

            public object GetData(Type format) => _wrappedObject.GetData(format);

            public object? GetData(string format, bool autoConvert)
            {
                if (autoConvert == false)
                {
                    return GetData(format);
                }

                return _wrappedObject.GetData(format, autoConvert);
            }

            public bool GetDataPresent(string format) => 
                format == ClipboardDataFormat
                    || _wrappedObject.GetDataPresent(format);

            public bool GetDataPresent(Type format) => _wrappedObject.GetDataPresent(format);

            public bool GetDataPresent(string format, bool autoConvert)
            {
                if (format != ClipboardDataFormat)
                {
                    return _wrappedObject.GetDataPresent(format, autoConvert);
                }

                return true;
            }

            public string[] GetFormats() => _wrappedObject.GetFormats().Append(ClipboardDataFormat).ToArray();

            public string[] GetFormats(bool autoConvert) => _wrappedObject.GetFormats(autoConvert).Append(ClipboardDataFormat).ToArray();

            public void SetData(object data) => _wrappedObject.SetData(data);

            public void SetData(string format, object data)
            {
                if (format == ClipboardDataFormat)
                {
                    throw new InvalidOperationException();
                }

                _wrappedObject.SetData(format, data);
            }

            public void SetData(Type format, object data) => _wrappedObject.SetData(format, data);

            public void SetData(string format, object data, bool autoConvert)
            {
                if (format == ClipboardDataFormat)
                {
                    throw new InvalidOperationException();
                }

                _wrappedObject.SetData(format, data, autoConvert);
            }
        }
    }
}
