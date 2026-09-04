using System;
using System.Runtime.CompilerServices;

namespace SpotnetEnc
{
    public class SpotnetDecoder
    {
        public void Init()
        {
            // Initializer for decoder lookup tables / SIMD state if required
        }

        public unsafe uint Decode(byte[] args, byte[] result, int start, uint arg_len)
        {
            if (args == null || result == null || arg_len == 0)
                return 0;

            uint written = 0;
            int end = (int)Math.Min((long)start + arg_len, (long)args.Length);
            int maxOut = result.Length;

            fixed (byte* pSrc = &args[0])
            fixed (byte* pDst = &result[0])
            {
                byte* src = pSrc + start;
                byte* srcEnd = pSrc + end;
                byte* dst = pDst;
                byte* dstEnd = pDst + maxOut;

                // The body arrives straight off the wire, so it is still dot-stuffed:
                // the server doubles a leading '.' on every line (RFC 3977 3.1.1).
                // Decoding starts at the first byte after the =ypart/=ybegin line,
                // which is a line boundary.
                bool atLineStart = true;

                while (src < srcEnd && dst < dstEnd)
                {
                    byte b = *src++;
                    if (b == (byte)'\r')
                    {
                        continue;
                    }
                    if (b == (byte)'\n')
                    {
                        atLineStart = true;
                        continue;
                    }

                    if (atLineStart)
                    {
                        atLineStart = false;
                        if (b == (byte)'.' && src < srcEnd && *src == (byte)'.')
                        {
                            // Drop the stuffing dot; the second one is payload.
                            src++;
                        }
                    }

                    if (b == (byte)'=') // Escape character in yEnc
                    {
                        if (src >= srcEnd) break;
                        byte next = *src++;
                        // Handle yEnc soft line breaks: '=\r\n'
                        if (next == (byte)'\r' || next == (byte)'\n')
                        {
                            if (src < srcEnd && (*src == (byte)'\r' || *src == (byte)'\n'))
                                src++;
                            atLineStart = true;
                            continue;
                        }
                        *dst++ = (byte)((next - 64 - 42) & 0xFF);
                        written++;
                    }
                    else
                    {
                        *dst++ = (byte)((b - 42) & 0xFF);
                        written++;
                    }
                }
            }

            return written;
        }
    }
}
