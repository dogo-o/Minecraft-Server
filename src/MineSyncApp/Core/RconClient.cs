using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MineSyncApp.Core
{
    public class RconClient
    {
        private const int TypeResponseValue = 0;
        private const int TypeExecCommand = 2;
        private const int TypeAuth = 3;

        private sealed class RconPacket
        {
            public int Id;
            public int Type;
            public string Body;
        }

        public static string StripFormatting(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return Regex.Replace(text, @"§[0-9a-fk-or]", "", RegexOptions.IgnoreCase);
        }

        private static byte[] EncodePacket(int id, int type, string body)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body ?? "");
            int packetLength = 4 + 4 + bodyBytes.Length + 2;
            byte[] buf = new byte[4 + packetLength];

            WriteInt32LE(buf, 0, packetLength);
            WriteInt32LE(buf, 4, id);
            WriteInt32LE(buf, 8, type);
            Buffer.BlockCopy(bodyBytes, 0, buf, 12, bodyBytes.Length);
            buf[12 + bodyBytes.Length] = 0;
            buf[13 + bodyBytes.Length] = 0;
            return buf;
        }

        private static void WriteInt32LE(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static int ReadInt32LE(byte[] buf, int offset)
        {
            return buf[offset]
                | (buf[offset + 1] << 8)
                | (buf[offset + 2] << 16)
                | (buf[offset + 3] << 24);
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, int timeoutMs)
        {
            byte[] buffer = new byte[count];
            int totalRead = 0;
            while (totalRead < count)
            {
                Task<int> readTask = stream.ReadAsync(buffer, totalRead, count - totalRead);
                Task completed = await Task.WhenAny(readTask, Task.Delay(timeoutMs)).ConfigureAwait(false);
                if (completed != readTask)
                {
                    throw new TimeoutException("Timeout aguardando resposta RCON.");
                }
                int n = await readTask.ConfigureAwait(false);
                if (n <= 0)
                {
                    throw new EndOfStreamException("Conexão RCON encerrada pelo servidor.");
                }
                totalRead += n;
            }
            return buffer;
        }

        private static async Task<RconPacket> ReadPacketAsync(NetworkStream stream, int timeoutMs)
        {
            byte[] lenBytes = await ReadExactAsync(stream, 4, timeoutMs).ConfigureAwait(false);
            int length = ReadInt32LE(lenBytes, 0);
            if (length < 10 || length > 1024 * 1024)
            {
                throw new InvalidDataException(string.Format("Tamanho de pacote RCON inválido: {0}", length));
            }
            byte[] payload = await ReadExactAsync(stream, length, timeoutMs).ConfigureAwait(false);
            RconPacket pkt = new RconPacket();
            pkt.Id = ReadInt32LE(payload, 0);
            pkt.Type = ReadInt32LE(payload, 4);
            int bodyLen = Math.Max(0, length - 10);
            pkt.Body = Encoding.UTF8.GetString(payload, 8, bodyLen);
            return pkt;
        }

        public static async Task<string> SendCommandAsync(ServerConfig config, string command, int timeoutMs)
        {
            Dictionary<string, string> props = config.ReadServerProperties();
            string enabledVal;
            if (props.TryGetValue("enable-rcon", out enabledVal) &&
                string.Equals(enabledVal.Trim(), "false", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("RCON está desativado em server.properties (enable-rcon=false).");
            }

            int port = 25575;
            string portVal;
            if (props.TryGetValue("rcon.port", out portVal))
            {
                int parsed;
                if (int.TryParse(portVal.Trim(), out parsed))
                {
                    port = parsed;
                }
            }

            string password;
            if (!props.TryGetValue("rcon.password", out password) || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("Senha RCON (rcon.password) não configurada em server.properties.");
            }

            return await SendCommandAsync("127.0.0.1", port, password, command, timeoutMs).ConfigureAwait(false);
        }

        public static async Task<string> SendCommandAsync(string host, int port, string password, string command, int timeoutMs)
        {
            using (TcpClient client = new TcpClient())
            {
                Task connectTask = client.ConnectAsync(host, port);
                Task done = await Task.WhenAny(connectTask, Task.Delay(timeoutMs)).ConfigureAwait(false);
                if (done != connectTask)
                {
                    throw new TimeoutException(string.Format("Timeout ao conectar via RCON em {0}:{1}", host, port));
                }
                await connectTask.ConfigureAwait(false);

                using (NetworkStream stream = client.GetStream())
                {
                    const int authId = 101;
                    const int cmdId = 202;

                    byte[] authPacket = EncodePacket(authId, TypeAuth, password);
                    await stream.WriteAsync(authPacket, 0, authPacket.Length).ConfigureAwait(false);

                    // Read auth response (skip optional empty type 0 packet)
                    while (true)
                    {
                        RconPacket pkt = await ReadPacketAsync(stream, timeoutMs).ConfigureAwait(false);
                        if (pkt.Type == TypeExecCommand)
                        {
                            if (pkt.Id == -1)
                            {
                                throw new UnauthorizedAccessException("Falha na autenticação RCON: senha incorreta.");
                            }
                            if (pkt.Id == authId)
                            {
                                break;
                            }
                        }
                    }

                    byte[] cmdPacket = EncodePacket(cmdId, TypeExecCommand, command);
                    await stream.WriteAsync(cmdPacket, 0, cmdPacket.Length).ConfigureAwait(false);

                    StringBuilder sb = new StringBuilder();
                    int readWindow = timeoutMs;
                    while (true)
                    {
                        try
                        {
                            RconPacket pkt = await ReadPacketAsync(stream, readWindow).ConfigureAwait(false);
                            if (pkt.Type == TypeResponseValue && pkt.Id == cmdId)
                            {
                                sb.Append(pkt.Body);
                                readWindow = 140;
                            }
                        }
                        catch (TimeoutException)
                        {
                            if (sb.Length > 0 || readWindow < timeoutMs)
                            {
                                break;
                            }
                            throw;
                        }
                    }

                    return StripFormatting(sb.ToString()).Trim();
                }
            }
        }
    }
}
