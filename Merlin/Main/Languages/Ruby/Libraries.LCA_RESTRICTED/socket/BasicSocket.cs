﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * ironruby@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

#if !SILVERLIGHT
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;
using IronRuby.Builtins;
using IronRuby.Runtime;
using IronRuby.StandardLibrary.FileControl;
using Microsoft.Scripting.Math;

namespace IronRuby.StandardLibrary.Sockets {
    [RubyClass("BasicSocket", BuildConfig = "!SILVERLIGHT")]
    public abstract class RubyBasicSocket : RubyIO {
        // TODO: do these escape out of the library?
        private static readonly MutableString BROADCAST_STRING = MutableString.CreateAscii("<broadcast>").Freeze();
        private static readonly MutableString BROADCAST_IP_STRING = MutableString.CreateAscii("255.255.255.255").Freeze();
        private static readonly MutableString ANY_IP_STRING = MutableString.CreateAscii("0.0.0.0").Freeze();

        private readonly Socket/*!*/ _socket;

        [MultiRuntimeAware]
        private static readonly object BasicSocketClassKey = new object();

        internal static StrongBox<bool> DoNotReverseLookup(RubyContext/*!*/ context) {
            Assert.NotNull(context);

            return (StrongBox<bool>)context.GetOrCreateLibraryData(BasicSocketClassKey, () => new StrongBox<bool>(false));
        }

        /// <summary>
        /// Create a new RubyBasicSocket from a specified stream and mode
        /// </summary>
        protected RubyBasicSocket(RubyContext/*!*/ context, Socket/*!*/ socket)
            : base(context, new SocketStream(socket), "rb+") {
            _socket = socket;
        }

        protected internal Socket/*!*/ Socket {
            get { return _socket; }
        }

        public override WaitHandle/*!*/ CreateReadWaitHandle() {
            return _socket.BeginReceive(Utils.EmptyBytes, 0, 0, SocketFlags.Peek, null, null).AsyncWaitHandle;
        }

        public override WaitHandle/*!*/ CreateWriteWaitHandle() {
            return _socket.BeginSend(Utils.EmptyBytes, 0, 0, SocketFlags.Peek, null, null).AsyncWaitHandle;
        }

        public override WaitHandle/*!*/ CreateErrorWaitHandle() {
            // TODO:
            throw new NotSupportedException();
        }

        // returns 0 on success, -1 on failure
        private int SetFileControlFlags(int flags) {
            // TODO:
            _socket.Blocking = (flags & RubyFileOps.Constants.NONBLOCK) != 0;
            return 0;
        }

        public override int FileControl(int commandId, int arg) {
            // TODO:
            switch (commandId) {
                case Fcntl.F_SETFL:
                    return SetFileControlFlags(arg);
            }
            throw new NotSupportedException();
        }

        public override int FileControl(int commandId, byte[] arg) {
            // TODO:
            throw new NotSupportedException();
        }

        #region Public Singleton Methods

        /// <summary>
        /// Returns the value of the global reverse lookup flag.
        /// </summary>
        [RubyMethod("do_not_reverse_lookup", RubyMethodAttributes.PublicSingleton)]
        public static bool GetDoNotReverseLookup(RubyContext/*!*/ context, RubyClass/*!*/ self) {
            return DoNotReverseLookup(context).Value;
        }

        /// <summary>
        /// Sets the value of the global reverse lookup flag.
        /// If set to true, queries on remote addresses will return the numeric address but not the host name.
        /// Defaults to false.
        /// </summary>
        [RubyMethod("do_not_reverse_lookup=", RubyMethodAttributes.PublicSingleton)]
        public static void SetDoNotReverseLookup(RubyContext/*!*/ context, RubyClass/*!*/ self, bool value) {
            Protocols.CheckSafeLevel(context, 4);
            DoNotReverseLookup(context).Value = value;
        }

        /// <summary>
        /// Wraps an already open file descriptor into a socket object.
        /// </summary>
        /// <returns>The corresponding socket</returns>
        [RubyMethod("for_fd", RubyMethodAttributes.PublicSingleton)]
        public static RubyBasicSocket/*!*/ ForFileDescriptor(RubyClass/*!*/ self, [DefaultProtocol]int fileDescriptor) {
            return (RubyBasicSocket)self.Context.GetDescriptor(fileDescriptor);
        }

        #endregion

        #region Public Instance Methods

        [RubyMethod("close_read")]
        public static void CloseRead(RubyContext/*!*/ context, RubyBasicSocket/*!*/ self) {
            CheckSecurity(context, self, "can't close socket");
            // TODO: It would be nice to alter the SocketStream to be WriteOnly here
            self.Socket.Shutdown(SocketShutdown.Receive);
        }

        [RubyMethod("close_write")]
        public static void CloseWrite(RubyContext/*!*/ context, RubyBasicSocket/*!*/ self) {
            CheckSecurity(context, self, "can't close socket");
            // TODO: It would be nice to alter the SocketStream to be ReadOnly here
            self.Socket.Shutdown(SocketShutdown.Send);
        }

        [RubyMethod("shutdown")]
        public static int Shutdown(RubyContext/*!*/ context, RubyBasicSocket/*!*/ self, [DefaultProtocol, DefaultParameterValue(2)]int how) {
            CheckSecurity(context, self, "can't shutdown socket");
            if (how < 0 || 2 < how) {
                throw RubyExceptions.CreateArgumentError("`how' should be either 0, 1, 2");
            }
            self.Socket.Shutdown((SocketShutdown)how);
            return 0;
        }

        /// <summary>
        /// Sets a socket option. These are protocol and system specific, see your local sytem documentation for details. 
        /// </summary>
        /// <param name="level">level is an integer, usually one of the SOL_ constants such as Socket::SOL_SOCKET, or a protocol level.</param>
        /// <param name="optname">optname is an integer, usually one of the SO_ constants, such as Socket::SO_REUSEADDR.</param>
        /// <param name="value">value is the value of the option, it is passed to the underlying setsockopt() as a pointer to a certain number of bytes. How this is done depends on the type.</param>
        [RubyMethod("setsockopt")]
        public static void SetSocketOption(ConversionStorage<int>/*!*/ conversionStorage, RubyContext/*!*/ context, 
            RubyBasicSocket/*!*/ self, [DefaultProtocol]int level, [DefaultProtocol]int optname, int value) {

            Protocols.CheckSafeLevel(context, 2, "setsockopt");
            self.Socket.SetSocketOption((SocketOptionLevel)level, (SocketOptionName)optname, value);
        }

        /// <summary>
        /// Sets a socket option. These are protocol and system specific, see your local sytem documentation for details. 
        /// </summary>
        /// <param name="level">level is an integer, usually one of the SOL_ constants such as Socket::SOL_SOCKET, or a protocol level.</param>
        /// <param name="optname">optname is an integer, usually one of the SO_ constants, such as Socket::SO_REUSEADDR.</param>
        /// <param name="value">value is the value of the option, it is passed to the underlying setsockopt() as a pointer to a certain number of bytes. How this is done depends on the type.</param>
        [RubyMethod("setsockopt")]
        public static void SetSocketOption(ConversionStorage<int>/*!*/ conversionStorage, RubyContext/*!*/ context, 
            RubyBasicSocket/*!*/ self, [DefaultProtocol]int level, [DefaultProtocol]int optname, bool value) {

            Protocols.CheckSafeLevel(context, 2, "setsockopt");
            self.Socket.SetSocketOption((SocketOptionLevel)level, (SocketOptionName)optname, value);
        }

        /// <summary>
        /// Sets a socket option. These are protocol and system specific, see your local sytem documentation for details. 
        /// </summary>
        /// <param name="level">level is an integer, usually one of the SOL_ constants such as Socket::SOL_SOCKET, or a protocol level.</param>
        /// <param name="optname">optname is an integer, usually one of the SO_ constants, such as Socket::SO_REUSEADDR.</param>
        /// <param name="value">value is the value of the option, it is passed to the underlying setsockopt() as a pointer to a certain number of bytes. How this is done depends on the type.</param>
        /// <example>
        /// Some socket options are integers with boolean values, in this case setsockopt could be called like this: 
        /// 
        ///   sock.setsockopt(Socket::SOL_SOCKET,Socket::SO_REUSEADDR, true)
        /// Some socket options are integers with numeric values, in this case setsockopt could be called like this: 
        /// 
        ///   sock.setsockopt(Socket::IPPROTO_IP, Socket::IP_TTL, 255)
        /// Option values may be structs. Passing them can be complex as it involves examining your system headers to determine the correct definition. An example is an ip_mreq, which may be defined in your system headers as: 
        /// 
        ///   struct ip_mreq {
        ///     struct  in_addr imr_multiaddr;
        ///     struct  in_addr imr_interface;
        ///   };
        /// In this case setsockopt could be called like this: 
        /// 
        ///   optval =  IPAddr.new("224.0.0.251") + Socket::INADDR_ANY
        ///   sock.setsockopt(Socket::IPPROTO_IP, Socket::IP_ADD_MEMBERSHIP, optval)
        /// </example>
        [RubyMethod("setsockopt")]
        public static void SetSocketOption(RubyContext/*!*/ context, RubyBasicSocket/*!*/ self, 
            [DefaultProtocol]int level, [DefaultProtocol]int optname, [DefaultProtocol, NotNull]MutableString/*!*/ value) {

            Protocols.CheckSafeLevel(context, 2, "setsockopt");
            self.Socket.SetSocketOption((SocketOptionLevel)level, (SocketOptionName)optname, value.ConvertToBytes());
        }

        /// <summary>
        /// Gets a socket option. These are protocol and system specific, see your local sytem documentation for details.
        /// </summary>
        /// <param name="level">level is an integer, usually one of the SOL_ constants such as Socket::SOL_SOCKET, or a protocol level.</param>
        /// <param name="optname">optname is an integer, usually one of the SO_ constants, such as Socket::SO_REUSEADDR.</param>
        /// <returns>The option is returned as a String with the data being the binary value of the socket option.</returns>
        /// <example>
        /// Some socket options are integers with boolean values, in this case getsockopt could be called like this: 
        ///  optval = sock.getsockopt(Socket::SOL_SOCKET,Socket::SO_REUSEADDR)
        ///  optval = optval.unpack "i"
        ///  reuseaddr = optval[0] == 0 ? false : true
        /// Some socket options are integers with numeric values, in this case getsockopt could be called like this: 
        ///  optval = sock.getsockopt(Socket::IPPROTO_IP, Socket::IP_TTL)
        ///  ipttl = optval.unpack("i")[0]
        /// Option values may be structs. Decoding them can be complex as it involves examining your system headers to determine the correct definition. An example is a +struct linger+, which may be defined in your system headers as: 
        ///   struct linger {
        ///     int l_onoff;
        ///     int l_linger;
        ///   };
        /// In this case getsockopt could be called like this: 
        ///   optval =  sock.getsockopt(Socket::SOL_SOCKET, Socket::SO_LINGER)
        ///   onoff, linger = optval.unpack "ii"
        /// </example>
        [RubyMethod("getsockopt")]
        public static MutableString GetSocketOption(ConversionStorage<int>/*!*/ conversionStorage, RubyContext/*!*/ context, 
            RubyBasicSocket/*!*/ self, [DefaultProtocol]int level, [DefaultProtocol]int optname) {
            Protocols.CheckSafeLevel(context, 2, "getsockopt");
            byte[] value = self.Socket.GetSocketOption((SocketOptionLevel)level, (SocketOptionName)optname, 4);
            return MutableString.CreateBinary(value);
        }

        [RubyMethod("getsockname")]
        public static MutableString GetSocketName(RubyBasicSocket/*!*/ self) {
            SocketAddress addr = self.Socket.LocalEndPoint.Serialize();
            byte[] bytes = new byte[addr.Size];
            for (int i = 0; i < addr.Size; ++i) {
                bytes[i] = addr[i];
            }
            return MutableString.CreateBinary(bytes);
        }

        [RubyMethod("getpeername")]
        public static MutableString GetPeerName(RubyBasicSocket/*!*/ self) {
            SocketAddress addr = self.Socket.RemoteEndPoint.Serialize();
            byte[] bytes = new byte[addr.Size];
            for (int i = 0; i < addr.Size; ++i) {
                bytes[i] = addr[i];
            }
            return MutableString.CreateBinary(bytes);
        }

        [RubyMethod("send")]
        public static int Send(ConversionStorage<int>/*!*/ fixnumCast, 
            RubyBasicSocket/*!*/ self, [DefaultProtocol, NotNull]MutableString/*!*/ message, object flags) {
            Protocols.CheckSafeLevel(fixnumCast.Context, 4, "send");
            SocketFlags socketFlags = ConvertToSocketFlag(fixnumCast, flags);
            return self.Socket.Send(message.ConvertToBytes(), socketFlags);
        }

        [RubyMethod("send")]
        public static int Send(ConversionStorage<int>/*!*/ fixnumCast, RubyBasicSocket/*!*/ self,
            [DefaultProtocol, NotNull]MutableString/*!*/ message, object flags, [DefaultProtocol, NotNull]MutableString/*!*/ to) {
            Protocols.CheckSafeLevel(fixnumCast.Context, 4, "send");
            // Convert the parameters
            SocketFlags socketFlags = ConvertToSocketFlag(fixnumCast, flags);
            // Unpack the socket address information from the to parameter
            SocketAddress address = new SocketAddress(AddressFamily.InterNetwork);
            for (int i = 0; i < to.GetByteCount(); i++) {
                address[i] = to.GetByte(i);
            }
            EndPoint toEndPoint = self.Socket.LocalEndPoint.Create(address);
            return self.Socket.SendTo(message.ConvertToBytes(), socketFlags, toEndPoint);
        }

        [RubyMethod("recv")]
        public static MutableString Receive(ConversionStorage<int>/*!*/ fixnumCast, RubyBasicSocket/*!*/ self, 
            [DefaultProtocol]int length, [DefaultParameterValue(null)]object flags) {

            SocketFlags sFlags = ConvertToSocketFlag(fixnumCast, flags);

            byte[] buffer = new byte[length];
            int received = self.Socket.Receive(buffer, 0, length, sFlags);

            MutableString str = MutableString.CreateBinary(received);
            str.Append(buffer, 0, received);
            fixnumCast.Context.SetObjectTaint(str, true);
            return str;
        }

        /// <summary>
        /// Receives up to length bytes from socket using recvfrom after O_NONBLOCK is set for the underlying file descriptor.
        /// </summary>
        /// <param name="length">Maximum number of bytes to receive</param>
        /// <param name="flags">flags is zero or more of the MSG_ options.</param>
        /// <returns>The data received. When recvfrom(2) returns 0, Socket#recv_nonblock returns an empty string as data. The meaning depends on the socket: EOF on TCP, empty packet on UDP, etc. </returns>
        /// <example>
        /// serv = TCPServer.new("127.0.0.1", 0)
        ///      af, port, host, addr = serv.addr
        ///      c = TCPSocket.new(addr, port)
        ///      s = serv.accept
        ///      c.send "aaa", 0
        ///      IO.select([s])
        ///      p s.recv_nonblock(10) #=> "aaa"
        /// </example>
        [RubyMethod("recv_nonblock")]
        public static MutableString/*!*/ ReceiveNonBlocking(ConversionStorage<int>/*!*/ fixnumCast, RubyBasicSocket/*!*/ self,
            [DefaultProtocol]int length, [DefaultParameterValue(null)]object flags) {

            bool blocking = self.Socket.Blocking;
            try {
                self.Socket.Blocking = false;
                return Receive(fixnumCast, self, length, flags);
            } finally {
                // Reset the blocking
                self.Socket.Blocking = blocking;
            }
        }

        #endregion

        #region Internal Helpers

        internal static void CheckSecurity(RubyContext/*!*/ context, object self, string message) {
            if (context.CurrentSafeLevel >= 4 && context.IsObjectTainted(self)) {
                throw RubyExceptions.CreateSecurityError("Insecure: " + message);
            }
        }

        // TODO: handle other invalid addresses
        internal static IPHostEntry/*!*/ GetHostEntry(IPAddress/*!*/ address) {
            Assert.NotNull(address);
            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.Loopback)) {
                return MakeEntry(address);
            } else {
                return Dns.GetHostEntry(address);
            }
        }

        // TODO: handle other invalid addresses
        internal static IPHostEntry/*!*/ GetHostEntry(string/*!*/ hostNameOrAddress) {
            Assert.NotNull(hostNameOrAddress);
            if (hostNameOrAddress == IPAddress.Any.ToString()) {
                return MakeEntry(IPAddress.Any);
            } else if (hostNameOrAddress == IPAddress.Loopback.ToString()) {
                return MakeEntry(IPAddress.Loopback);
            } else {
                return Dns.GetHostEntry(hostNameOrAddress);
            }
        }

        internal static IPAddress/*!*/ GetHostAddress(string/*!*/ hostNameOrAddress) {
            IPAddress address;
            if (IPAddress.TryParse(hostNameOrAddress, out address)) {
                return address;
            }
            // TODO: map exceptions
            return Dns.GetHostAddresses(hostNameOrAddress)[0];
        }

        internal static IPHostEntry/*!*/ MakeEntry(IPAddress/*!*/ address) {
            return new IPHostEntry() {
                AddressList = new[] { address },
                Aliases = new[] { address.ToString() },
                HostName = address.ToString()
            };
        }

        internal static RubyArray/*!*/ GetHostByName(string/*!*/ hostNameOrAddress, bool packIpAddresses) {
            return CreateHostEntryArray(GetHostEntry(hostNameOrAddress), packIpAddresses);
        }

        internal static RubyArray/*!*/ GetAddressArray(RubyContext/*!*/ context, EndPoint endPoint) {
            RubyArray result = new RubyArray(4);

            IPEndPoint ep = (IPEndPoint)endPoint;

            result.Add(MutableString.CreateAscii(AddressFamilyToString(ep.AddressFamily)));
            result.Add(ep.Port);
            if (DoNotReverseLookup(context).Value) {
                result.Add(MutableString.CreateAscii(ep.Address.ToString()));
            } else {
                // TODO: MRI returns localhost rather than the local machine name here
                // TODO (encoding):
                result.Add(MutableString.Create(Dns.GetHostEntry(ep.Address).HostName, RubyEncoding.UTF8));
            }
            result.Add(MutableString.CreateAscii(ep.Address.ToString()));
            return result;
        }

        private static string AddressFamilyToString(AddressFamily af) {
            // for the most part we can just use the upper-cased AddressFamily name
            // of the enum value, but for some types we need to explicitly map the
            // correct names
            switch (af) {
                case AddressFamily.InterNetwork: return "AF_INET";
                case AddressFamily.DataLink: return "AF_DLI";
                case AddressFamily.HyperChannel: return "AF_HYLINK";
                case AddressFamily.Banyan: return "AF_BAN";
                case AddressFamily.InterNetworkV6: return "AF_INET6";
                case AddressFamily.Ieee12844: return "AF_12844";
                case AddressFamily.NetworkDesigners: return "AF_NETDES";
                default:
                    string name = Enum.GetName(typeof(AddressFamily), af);
                    return (name != null) ?
                        "AF_" + name.ToUpper() :
                        "unknown:" + ((int)af).ToString();
            }
        }

        internal static SocketFlags ConvertToSocketFlag(ConversionStorage<int>/*!*/ conversionStorage, object flags) {
            if (flags == null) {
                return SocketFlags.None;
            }
            return (SocketFlags)Protocols.CastToFixnum(conversionStorage, flags);
        }

        internal static AddressFamily ConvertToAddressFamily(ConversionStorage<MutableString>/*!*/ stringCast, ConversionStorage<int>/*!*/ fixnumCast, 
            object family) {

            // Default is AF_INET
            if (family == null) {
                return AddressFamily.InterNetwork;
            }
            // If it is a Fixnum then assume it is just the constant value
            if (family is int) {
                return (AddressFamily)(int)family;
            }
            // Convert to a string (using to_str) and then look up the value
            MutableString strFamily = Protocols.CastToString(stringCast, family);
            foreach (AddressFamilyName name in FamilyNames) {
                if (name.Name.Equals(strFamily)) {
                    return name.Family;
                }
            }
            // Convert to a Fixnum (using to_i) and hope it is a valid AddressFamily constant
            return (AddressFamily)Protocols.CastToFixnum(fixnumCast, strFamily);
        }

        internal static MutableString ToAddressFamilyString(AddressFamily family) {
            foreach (AddressFamilyName name in FamilyNames) {
                if (name.Family == family) {
                    return name.Name;
                }
            }
            throw new SocketException((int)SocketError.AddressFamilyNotSupported);
        }


        internal static string/*!*/ ConvertToHostString(uint address) {
            // Ruby uses Little Endian whereas .NET uses Big Endian IP values
            byte[] bytes = new byte[4];
            for (int i = bytes.Length - 1; i >= 0; --i) {
                bytes[i] = (byte)(address & 0xff);
                address >>= 8;
            }
            return new IPAddress(bytes).ToString();
        }

        internal static string/*!*/ ConvertToHostString(BigInteger/*!*/ address) {
            Assert.NotNull(address);
            ulong u;
            if (address.AsUInt64(out u)) {
                if (u <= UInt32.MaxValue) {
                    // IPv4:
                    return ConvertToHostString((uint)u);
                } else {
                    // IPv6:
                    byte[] bytes = new byte[8];
                    for (int i = bytes.Length - 1; i >= 0; --i) {
                        bytes[i] = (byte)(u & 0xff);
                        u >>= 8;
                    }
                    return new IPAddress(bytes).ToString();
                }
            } else {
                throw RubyExceptions.CreateRangeError("bignum too big to convert into `quad long'");
            }
        }

        internal static string/*!*/ ConvertToHostString(MutableString hostName) {
            if (hostName == null) {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            if (hostName.IsEmpty) {
                return IPAddress.Any.ToString();
            } else if (hostName.Equals(BROADCAST_STRING)) {
                return IPAddress.Broadcast.ToString();
            }
            return hostName.ConvertToString();
        }

        internal static string/*!*/ ConvertToHostString(ConversionStorage<MutableString>/*!*/ stringCast, object hostName) {
            BigInteger bignum;
            if (hostName is int) {
                return ConvertToHostString((int)hostName);
            } else if (!ReferenceEquals(bignum = hostName as BigInteger, null)) {
                return ConvertToHostString(bignum);
            } else if (hostName != null) {
                return ConvertToHostString(Protocols.CastToString(stringCast, hostName));
            } else {
                return ConvertToHostString((MutableString)null);
            }
        }
        
        /// <summary>
        /// Converts an Integer to a Fixnum.
        /// Don't call any conversion methods--just handles Fixnum & Bignum
        /// </summary>
        /// <param name="value"></param>
        /// <returns>true if value is an Integer, false otherwise</returns>
        /// <exception cref="ArgumentOutOfRangeException">Throws a RangeError if value is a
        /// BigInteger but can't be converted to a Fixnum</exception>
        internal static bool IntegerAsFixnum(object value, out int result) {
            if (value is int) {
                result = (int)value;
                return true;
            }

            var bignum = value as BigInteger;
            if ((object)bignum != null) {
                if (!bignum.AsInt32(out result)) {
                    throw RubyExceptions.CreateRangeError("bignum too big to convert into `long'");
                }
                return true;
            }

            result = 0;
            return false;
        }

        internal static int ConvertToPortNum(ConversionStorage<MutableString>/*!*/ stringCast, ConversionStorage<int>/*!*/ fixnumCast, object port) {
            // conversion protocol: if it's a Fixnum, return it
            // otherwise, convert to string & then convert the result to a Fixnum
            if (port is int) {
                return (int)port;
            }

            if (port == null) {
                return 0;
            }

            MutableString serviceName = Protocols.CastToString(stringCast, port);
            ServiceName service = SearchForService(serviceName);
            if (service != null) {
                return service.Port;
            }

            return Protocols.CastToFixnum(fixnumCast, serviceName);
        }

        internal static ServiceName SearchForService(int port) {
            foreach (ServiceName name in ServiceNames) {
                if (name.Port == port) {
                    return name;
                }
            }
            return null;
        }

        internal static ServiceName SearchForService(MutableString/*!*/ serviceName) {
            foreach (ServiceName name in ServiceNames) {
                if (name.Name.Equals(serviceName)) {
                    return name;
                }
            }
            return null;
        }

        internal static ServiceName SearchForService(MutableString/*!*/ serviceName, MutableString/*!*/ protocol) {
            foreach (ServiceName name in ServiceNames) {
                if (name.Name.Equals(serviceName) && name.Protocol.Equals(protocol)) {
                    return name;
                }
            }
            return null;
        }

        internal static RubyArray/*!*/ CreateHostEntryArray(IPHostEntry/*!*/ hostEntry, bool packIpAddresses) {
            RubyArray result = new RubyArray(4);
            // Canonical Hostname
            // TODO (encoding):
            result.Add(MutableString.Create(hostEntry.HostName, RubyEncoding.UTF8));

            // Aliases
            RubyArray aliases = new RubyArray(hostEntry.Aliases.Length);
            foreach (string alias in hostEntry.Aliases) {
                // TODO (encoding):
                aliases.Add(MutableString.Create(alias, RubyEncoding.UTF8));
            }
            result.Add(aliases);

            // Address Type
            result.Add((int)hostEntry.AddressList[0].AddressFamily);

            // IP Address
            foreach (IPAddress address in hostEntry.AddressList) {
                if (packIpAddresses) {
                    byte[] bytes = address.GetAddressBytes();
                    MutableString str = MutableString.CreateBinary();
                    str.Append(bytes, 0, bytes.Length);
                    result.Add(str);
                } else {
                    result.Add(MutableString.CreateAscii(address.ToString()));
                }
            }
            return result;
        }


        private struct AddressFamilyName {
            public readonly MutableString/*!*/ Name;
            public readonly AddressFamily Family;

            public AddressFamilyName(string/*!*/ name, AddressFamily family) {
                Name = MutableString.CreateAscii(name);
                Family = family;
            }
        }

        private static AddressFamilyName[] FamilyNames = new[] {
            new AddressFamilyName("AF_INET", AddressFamily.InterNetwork),
            new AddressFamilyName("AF_UNIX", AddressFamily.Unix),
            //new AddressFamilyName("AF_AX25", AddressFamily.Ax),
            new AddressFamilyName("AF_IPX", AddressFamily.Ipx),
            new AddressFamilyName("AF_APPLETALK", AddressFamily.AppleTalk),
            new AddressFamilyName("AF_UNSPEC", AddressFamily.Unspecified),
            new AddressFamilyName("AF_INET6", AddressFamily.InterNetworkV6),
            //new AddressFamilyName("AF_LOCAL", AddressFamily.Local),
            new AddressFamilyName("AF_IMPLINK", AddressFamily.ImpLink),
            new AddressFamilyName("AF_PUP", AddressFamily.Pup),
            new AddressFamilyName("AF_CHAOS", AddressFamily.Chaos),
            new AddressFamilyName("AF_NS", AddressFamily.NS),
            new AddressFamilyName("AF_ISO", AddressFamily.Iso),
            new AddressFamilyName("AF_OSI", AddressFamily.Osi),
            new AddressFamilyName("AF_ECMA", AddressFamily.Ecma),
            new AddressFamilyName("AF_DATAKIT", AddressFamily.DataKit),
            new AddressFamilyName("AF_CCITT", AddressFamily.Ccitt),
            new AddressFamilyName("AF_SNA", AddressFamily.Sna),
            new AddressFamilyName("AF_DEC", AddressFamily.DecNet),
            new AddressFamilyName("AF_DLI", AddressFamily.DataLink),
            new AddressFamilyName("AF_LAT", AddressFamily.Lat),
            new AddressFamilyName("AF_HYLINK", AddressFamily.HyperChannel),
            //new AddressFamilyName("AF_ROUTE", AddressFamily.Route),
            //new AddressFamilyName("AF_LINK", AddressFamily.Link),
            //new AddressFamilyName("AF_COIP", AddressFamily.Coip),
            //new AddressFamilyName("AF_CNT", AddressFamily.Cnt),
            //new AddressFamilyName("AF_SIP", AddressFamily.Sip),
            //new AddressFamilyName("AF_NDRV", AddressFamily.Nrdv),
            //new AddressFamilyName("AF_ISDN", AddressFamily.Isdn),
            //new AddressFamilyName("AF_NATM", AddressFamily.NATM),
            //new AddressFamilyName("AF_SYSTEM", AddressFamily.System),
            new AddressFamilyName("AF_NETBIOS", AddressFamily.NetBios),
            //new AddressFamilyName("AF_PPP", AddressFamily.Ppp),
            new AddressFamilyName("AF_ATM", AddressFamily.Atm),
            //new AddressFamilyName("AF_NETGRAPH", AddressFamily.Netgraph),
            new AddressFamilyName("AF_MAX", AddressFamily.Max),
            //new AddressFamilyName("AF_E164", AddressFamily.E164),
        };

        internal sealed class ServiceName {
            public readonly int Port;
            public readonly MutableString Protocol;
            public readonly MutableString Name;

            public ServiceName(int port, string/*!*/ protocol, string/*!*/ name) {
                Port = port;
                Protocol = MutableString.CreateAscii(protocol);
                Name = MutableString.CreateAscii(name);
            }
        }

        private static ServiceName[] ServiceNames = new[] {
            new ServiceName(7, "tcp", "echo"),
            new ServiceName(7, "udp", "echo"),
            new ServiceName(9, "tcp", "discard"),
            new ServiceName(9, "udp", "discard"),
            new ServiceName(11, "tcp", "systat"),
            new ServiceName(11, "udp", "systat"),
            new ServiceName(13, "tcp", "daytime"),
            new ServiceName(13, "udp", "daytime"),
            new ServiceName(15, "tcp", "netstat"),
            new ServiceName(17, "tcp", "qotd"),
            new ServiceName(17, "udp", "qotd"),
            new ServiceName(19, "tcp", "chargen"),
            new ServiceName(19, "udp", "chargen"),
            new ServiceName(20, "tcp", "ftp-data"),
            new ServiceName(21, "tcp", "ftp"),
            new ServiceName(23, "tcp", "telnet"),
            new ServiceName(25, "tcp", "smtp"),
            new ServiceName(37, "tcp", "time"),
            new ServiceName(37, "udp", "time"),
            new ServiceName(39, "udp", "rlp"),
            new ServiceName(42, "tcp", "name"),
            new ServiceName(42, "udp", "name"),
            new ServiceName(43, "tcp", "whois"),
            new ServiceName(53, "tcp", "domain"),
            new ServiceName(53, "udp", "domain"),
            new ServiceName(53, "tcp", "nameserver"),
            new ServiceName(53, "udp", "nameserver"),
            new ServiceName(57, "tcp", "mtp"),
            new ServiceName(67, "udp", "bootp"),
            new ServiceName(69, "udp", "tftp"),
            new ServiceName(77, "tcp", "rje"),
            new ServiceName(79, "tcp", "finger"),
            new ServiceName(80, "tcp", "http"),
            new ServiceName(87, "tcp", "link"),
            new ServiceName(95, "tcp", "supdup"),
            new ServiceName(101, "tcp", "hostnames"),
            new ServiceName(102, "tcp", "iso-tsap"),
            new ServiceName(103, "tcp", "dictionary"),
            new ServiceName(103, "tcp", "x400"),
            new ServiceName(104, "tcp", "x400-snd"),
            new ServiceName(105, "tcp", "csnet-ns"),
            new ServiceName(109, "tcp", "pop"),
            new ServiceName(109, "tcp", "pop2"),
            new ServiceName(110, "tcp", "pop3"),
            new ServiceName(111, "tcp", "portmap"),
            new ServiceName(111, "udp", "portmap"),
            new ServiceName(111, "tcp", "sunrpc"),
            new ServiceName(111, "udp", "sunrpc"),
            new ServiceName(113, "tcp", "auth"),
            new ServiceName(115, "tcp", "sftp"),
            new ServiceName(117, "tcp", "path"),
            new ServiceName(117, "tcp", "uucp-path"),
            new ServiceName(119, "tcp", "nntp"),
            new ServiceName(123, "udp", "ntp"),
            new ServiceName(137, "udp", "nbname"),
            new ServiceName(138, "udp", "nbdatagram"),
            new ServiceName(139, "tcp", "nbsession"),
            new ServiceName(144, "tcp", "NeWS"),
            new ServiceName(153, "tcp", "sgmp"),
            new ServiceName(158, "tcp", "tcprepo"),
            new ServiceName(161, "tcp", "snmp"),
            new ServiceName(162, "tcp", "snmp-trap"),
            new ServiceName(170, "tcp", "print-srv"),
            new ServiceName(175, "tcp", "vmnet"),
            new ServiceName(315, "udp", "load"),
            new ServiceName(400, "tcp", "vmnet0"),
            new ServiceName(500, "udp", "sytek"),
            new ServiceName(512, "udp", "biff"),
            new ServiceName(512, "tcp", "exec"),
            new ServiceName(513, "tcp", "login"),
            new ServiceName(513, "udp", "who"),
            new ServiceName(514, "tcp", "shell"),
            new ServiceName(514, "udp", "syslog"),
            new ServiceName(515, "tcp", "printer"),
            new ServiceName(517, "udp", "talk"),
            new ServiceName(518, "udp", "ntalk"),
            new ServiceName(520, "tcp", "efs"),
            new ServiceName(520, "udp", "route"),
            new ServiceName(525, "udp", "timed"),
            new ServiceName(526, "tcp", "tempo"),
            new ServiceName(530, "tcp", "courier"),
            new ServiceName(531, "tcp", "conference"),
            new ServiceName(531, "udp", "rvd-control"),
            new ServiceName(532, "tcp", "netnews"),
            new ServiceName(533, "udp", "netwall"),
            new ServiceName(540, "tcp", "uucp"),
            new ServiceName(543, "tcp", "klogin"),
            new ServiceName(544, "tcp", "kshell"),
            new ServiceName(550, "udp", "new-rwho"),
            new ServiceName(556, "tcp", "remotefs"),
            new ServiceName(560, "udp", "rmonitor"),
            new ServiceName(561, "udp", "monitor"),
            new ServiceName(600, "tcp", "garcon"),
            new ServiceName(601, "tcp", "maitrd"),
            new ServiceName(602, "tcp", "busboy"),
            new ServiceName(700, "udp", "acctmaster"),
            new ServiceName(701, "udp", "acctslave"),
            new ServiceName(702, "udp", "acct"),
            new ServiceName(703, "udp", "acctlogin"),
            new ServiceName(704, "udp", "acctprinter"),
            new ServiceName(704, "udp", "elcsd"),
            new ServiceName(705, "udp", "acctinfo"),
            new ServiceName(706, "udp", "acctslave2"),
            new ServiceName(707, "udp", "acctdisk"),
            new ServiceName(750, "tcp", "kerberos"),
            new ServiceName(750, "udp", "kerberos"),
            new ServiceName(751, "tcp", "kerberos_master"),
            new ServiceName(751, "udp", "kerberos_master"),
            new ServiceName(752, "udp", "passwd_server"),
            new ServiceName(753, "udp", "userreg_server"),
            new ServiceName(754, "tcp", "krb_prop"),
            new ServiceName(888, "tcp", "erlogin"),
            new ServiceName(1109, "tcp", "kpop"),
            new ServiceName(1167, "udp", "phone"),
            new ServiceName(1524, "tcp", "ingreslock"),
            new ServiceName(1666, "udp", "maze"),
            new ServiceName(2049, "udp", "nfs"),
            new ServiceName(2053, "tcp", "knetd"),
            new ServiceName(2105, "tcp", "eklogin"),
            new ServiceName(5555, "tcp", "rmt"),
            new ServiceName(5556, "tcp", "mtb"),
            new ServiceName(9535, "tcp", "man"),
            new ServiceName(9536, "tcp", "w"),
            new ServiceName(9537, "tcp", "mantst"),
            new ServiceName(10000, "tcp", "bnews"),
            new ServiceName(10000, "udp", "rscs0"),
            new ServiceName(10001, "tcp", "queue"),
            new ServiceName(10001, "udp", "rscs1"),
            new ServiceName(10002, "tcp", "poker"),
            new ServiceName(10002, "udp", "rscs2"),
            new ServiceName(10003, "tcp", "gateway"),
            new ServiceName(10003, "udp", "rscs3"),
            new ServiceName(10004, "tcp", "remp"),
            new ServiceName(10004, "udp", "rscs4"),
            new ServiceName(10005, "udp", "rscs5"),
            new ServiceName(10006, "udp", "rscs6"),
            new ServiceName(10007, "udp", "rscs7"),
            new ServiceName(10008, "udp", "rscs8"),
            new ServiceName(10009, "udp", "rscs9"),
            new ServiceName(10010, "udp", "rscsa"),
            new ServiceName(10011, "udp", "rscsb"),
            new ServiceName(10012, "tcp", "qmaster"),
            new ServiceName(10012, "udp", "qmaster")
        };

        #endregion
    }
}
#endif
