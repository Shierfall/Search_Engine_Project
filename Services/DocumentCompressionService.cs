using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace SearchEngine.Services
{
    public class DocumentCompressionService
    {
        // compress a string to a byte array
        public byte[] Compress(string content)
        {
            if (string.IsNullOrEmpty(content))
                return Array.Empty<byte>();
                
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            
            using var outputStream = new MemoryStream();
            using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
            {
                gzipStream.Write(contentBytes, 0, contentBytes.Length);
            }
            
            return outputStream.ToArray();
        }
        
        // decompress a byte array to a string
        public string Decompress(byte[] compressedContent)
        {
            if (compressedContent == null || compressedContent.Length == 0)
                return string.Empty;
                
            using var inputStream = new MemoryStream(compressedContent);
            using var outputStream = new MemoryStream();
            using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
            {
                gzipStream.CopyTo(outputStream);
            }
            
            return Encoding.UTF8.GetString(outputStream.ToArray());
        }
    }
} 