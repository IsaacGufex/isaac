﻿//
// MailService.cs
//
// Author: Jeffrey Stedfast <jestedfa@microsoft.com>
//
// Copyright (c) 2013-2021 .NET Foundation and Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Net.Security;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using SslProtocols = System.Security.Authentication.SslProtocols;

using MailKit.Net;
using MailKit.Net.Proxy;
using MailKit.Security;

using NetworkStream = MailKit.Net.NetworkStream;

namespace MailKit {
	/// <summary>
	/// An abstract mail service implementation.
	/// </summary>
	/// <remarks>
	/// An abstract mail service implementation.
	/// </remarks>
	public abstract class MailService : IMailService
	{
#if NET48 || NET5_0_OR_GREATER
		const SslProtocols DefaultSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
#else
		const SslProtocols DefaultSslProtocols = SslProtocols.Tls12 | (SslProtocols) 12288;
#endif

		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.MailService"/> class.
		/// </summary>
		/// <remarks>
		/// Initializes a new instance of the <see cref="MailKit.MailService"/> class.
		/// </remarks>
		/// <param name="protocolLogger">The protocol logger.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="protocolLogger"/> is <c>null</c>.
		/// </exception>
		protected MailService (IProtocolLogger protocolLogger)
		{
			if (protocolLogger == null)
				throw new ArgumentNullException (nameof (protocolLogger));

			SslProtocols = DefaultSslProtocols;
			CheckCertificateRevocation = true;
			ProtocolLogger = protocolLogger;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.MailService"/> class.
		/// </summary>
		/// <remarks>
		/// Initializes a new instance of the <see cref="MailKit.MailService"/> class.
		/// </remarks>
		protected MailService () : this (new NullProtocolLogger ())
		{
		}

		/// <summary>
		/// Releases unmanaged resources and performs other cleanup operations before the
		/// <see cref="MailService"/> is reclaimed by garbage collection.
		/// </summary>
		/// <remarks>
		/// Releases unmanaged resources and performs other cleanup operations before the
		/// <see cref="MailService"/> is reclaimed by garbage collection.
		/// </remarks>
		~MailService ()
		{
			Dispose (false);
		}

		/// <summary>
		/// Gets an object that can be used to synchronize access to the service.
		/// </summary>
		/// <remarks>
		/// <para>Gets an object that can be used to synchronize access to the service.</para>
		/// </remarks>
		/// <value>The sync root.</value>
		public abstract object SyncRoot {
			get;
		}

		/// <summary>
		/// Gets the protocol supported by the message service.
		/// </summary>
		/// <remarks>
		/// Gets the protocol supported by the message service.
		/// </remarks>
		/// <value>The protocol.</value>
		protected abstract string Protocol {
			get;
		}

		/// <summary>
		/// Get the protocol logger.
		/// </summary>
		/// <remarks>
		/// Gets the protocol logger.
		/// </remarks>
		/// <value>The protocol logger.</value>
		public IProtocolLogger ProtocolLogger {
			get; private set;
		}

		/// <summary>
		/// Gets or sets the set of enabled SSL and/or TLS protocol versions that the client is allowed to use.
		/// </summary>
		/// <remarks>
		/// <para>Gets or sets the enabled SSL and/or TLS protocol versions that the client is allowed to use.</para>
		/// <para>By default, MailKit initializes this value to enable only TLS v1.2 and greater.
		/// TLS v1.1, TLS v1.0 and all versions of SSL are not enabled by default due to them all being
		/// susceptible to security vulnerabilities such as POODLE.</para>
		/// <para>This property should be set before calling any of the
		/// <a href="Overload_MailKit_MailService_Connect.htm">Connect</a> methods.</para>
		/// </remarks>
		/// <value>The SSL and TLS protocol versions that are enabled.</value>
		public SslProtocols SslProtocols {
			get; set;
		}

#if NET5_0_OR_GREATER
		/// <summary>
		/// Gets or sets the cipher suites allowed to be used when negotiating an SSL or TLS connection.
		/// </summary>
		/// <remarks>
		/// Specifies the cipher suites allowed to be used when negotiating an SSL or TLS connection.
		/// When set to <c>null</c>, the operating system default is used. Use extreme caution when
		/// changing this setting.
		/// </remarks>
		/// <value>The cipher algorithms allowed for use when negotiating SSL or TLS encryption.</value>
		public CipherSuitesPolicy SslCipherSuitesPolicy {
			get; set;
		}
#endif

		/// <summary>
		/// Gets or sets the client SSL certificates.
		/// </summary>
		/// <remarks>
		/// <para>Some servers may require the client SSL certificates in order
		/// to allow the user to connect.</para>
		/// <para>This property should be set before calling any of the
		/// <a href="Overload_MailKit_MailService_Connect.htm">Connect</a> methods.</para>
		/// </remarks>
		/// <value>The client SSL certificates.</value>
		public X509CertificateCollection ClientCertificates {
			get; set;
		}

		/// <summary>
		/// Get or set whether connecting via SSL/TLS should check certificate revocation.
		/// </summary>
		/// <remarks>
		/// <para>Gets or sets whether connecting via SSL/TLS should check certificate revocation.</para>
		/// <para>Normally, the value of this property should be set to <c>true</c> (the default) for security
		/// reasons, but there are times when it may be necessary to set it to <c>false</c>.</para>
		/// <para>For example, most Certificate Authorities are probably pretty good at keeping their CRL and/or
		/// OCSP servers up 24/7, but occasionally they do go down or are otherwise unreachable due to other
		/// network problems between the client and the Certificate Authority. When this happens, it becomes
		/// impossible to check the revocation status of one or more of the certificates in the chain
		/// resulting in an <see cref="SslHandshakeException"/> being thrown in the
		/// <a href="Overload_MailKit_MailService_Connect.htm">Connect</a> method. If this becomes a problem,
		/// it may become desirable to set <see cref="CheckCertificateRevocation"/> to <c>false</c>.</para>
		/// </remarks>
		/// <value><c>true</c> if certificate revocation should be checked; otherwise, <c>false</c>.</value>
		public bool CheckCertificateRevocation {
			get; set;
		}

		/// <summary>
		/// Get or sets a callback function to validate the server certificate.
		/// </summary>
		/// <remarks>
		/// <para>Gets or sets a callback function to validate the server certificate.</para>
		/// <para>This property should be set before calling any of the
		/// <a href="Overload_MailKit_MailService_Connect.htm">Connect</a> methods.</para>
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\SslCertificateValidation.cs"/>
		/// </example>
		/// <value>The server certificate validation callback function.</value>
		public RemoteCertificateValidationCallback ServerCertificateValidationCallback {
			get; set;
		}

		/// <summary>
		/// Get or set the local IP end point to use when connecting to the remote host.
		/// </summary>
		/// <remarks>
		/// Gets or sets the local IP end point to use when connecting to the remote host.
		/// </remarks>
		/// <value>The local IP end point or <c>null</c> to use the default end point.</value>
		public IPEndPoint LocalEndPoint {
			get; set;
		}

		/// <summary>
		/// Get or set the proxy client to use when connecting to a remote host.
		/// </summary>
		/// <remarks>
		/// Gets or sets the proxy client to use when connecting to a remote host via any of the
		/// <a href="Overload_MailKit_MailService_Connect.htm">Connect</a> methods.
		/// </remarks>
		/// <value>The proxy client.</value>
		public IProxyClient ProxyClient {
			get; set;
		}

		/// <summary>
		/// Gets the authentication mechanisms supported by the mail server.
		/// </summary>
		/// <remarks>
		/// The authentication mechanisms are queried as part of the
		/// <a href="Overload_MailKit_MailService_Connect.htm">Connect</a> method.
		/// </remarks>
		/// <value>The authentication mechanisms.</value>
		public abstract HashSet<string> AuthenticationMechanisms {
			get;
		}

		/// <summary>
		/// Gets whether or not the client is currently connected to an mail server.
		/// </summary>
		/// <remarks>
		///<para>The <see cref="IsConnected"/> state is set to <c>true</c> immediately after
		/// one of the <a href="Overload_MailKit_MailService_Connect.htm">Connect</a>
		/// methods succeeds and is not set back to <c>false</c> until either the client
		/// is disconnected via <see cref="Disconnect(bool,CancellationToken)"/> or until a
		/// <see cref="ProtocolException"/> is thrown while attempting to read or write to
		/// the underlying network socket.</para>
		/// <para>When an <see cref="ProtocolException"/> is caught, the connection state of the
		/// <see cref="MailService"/> should be checked before continuing.</para>
		/// </remarks>
		/// <value><c>true</c> if the client is connected; otherwise, <c>false</c>.</value>
		public abstract bool IsConnected {
			get;
		}

		/// <summary>
		/// Get whether or not the connection is secure (typically via SSL or TLS).
		/// </summary>
		/// <remarks>
		/// Gets whether or not the connection is secure (typically via SSL or TLS).
		/// </remarks>
		/// <value><c>true</c> if the connection is secure; otherwise, <c>false</c>.</value>
		public abstract bool IsSecure {
			get;
		}

		/// <summary>
		/// Get whether or not the connection is encrypted (typically via SSL or TLS).
		/// </summary>
		/// <remarks>
		/// Gets whether or not the connection is encrypted (typically via SSL or TLS).
		/// </remarks>
		/// <value><c>true</c> if the connection is encrypted; otherwise, <c>false</c>.</value>
		public abstract bool IsEncrypted {
			get;
		}

		/// <summary>
		/// Get whether or not the connection is signed (typically via SSL or TLS).
		/// </summary>
		/// <remarks>
		/// Gets whether or not the connection is signed (typically via SSL or TLS).
		/// </remarks>
		/// <value><c>true</c> if the connection is signed; otherwise, <c>false</c>.</value>
		public abstract bool IsSigned {
			get;
		}

		/// <summary>
		/// Get the negotiated SSL or TLS protocol version.
		/// </summary>
		/// <remarks>
		/// <para>Gets the negotiated SSL or TLS protocol version once an SSL or TLS connection has been made.</para>
		/// </remarks>
		/// <value>The negotiated SSL or TLS protocol version.</value>
		public abstract SslProtocols SslProtocol {
			get;
		}

		/// <summary>
		/// Get the negotiated SSL or TLS cipher algorithm.
		/// </summary>
		/// <remarks>
		/// Gets the negotiated SSL or TLS cipher algorithm once an SSL or TLS connection has been made.
		/// </remarks>
		/// <value>The negotiated SSL or TLS cipher algorithm.</value>
		public abstract CipherAlgorithmType? SslCipherAlgorithm {
			get;
		}

		/// <summary>
		/// Get the negotiated SSL or TLS cipher algorithm strength.
		/// </summary>
		/// <remarks>
		/// Gets the negotiated SSL or TLS cipher algorithm strength once an SSL or TLS connection has been made.
		/// </remarks>
		/// <value>The negotiated SSL or TLS cipher algorithm strength.</value>
		public abstract int? SslCipherStrength {
			get;
		}

		/// <summary>
		/// Get the negotiated SSL or TLS hash algorithm.
		/// </summary>
		/// <remarks>
		/// Gets the negotiated SSL or TLS hash algorithm once an SSL or TLS connection has been made.
		/// </remarks>
		/// <value>The negotiated SSL or TLS hash algorithm.</value>
		public abstract HashAlgorithmType? SslHashAlgorithm {
			get;
		}

		/// <summary>
		/// Get the negotiated SSL or TLS hash algorithm strength.
		/// </summary>
		/// <remarks>
		/// Gets the negotiated SSL or TLS hash algorithm strength once an SSL or TLS connection has been made.
		/// </remarks>
		/// <value>The negotiated SSL or TLS hash algorithm strength.</value>
		public abstract int? SslHashStrength {
			get;
		}

		/// <summary>
		/// Get the negotiated SSL or TLS key exchange algorithm.
		/// </summary>
		/// <remarks>
		/// Gets the negotiated SSL or TLS key exchange algorithm once an SSL or TLS connection has been made.
		/// </remarks>
		/// <value>The negotiated SSL or TLS key exchange algorithm.</value>
		public abstract ExchangeAlgorithmType? SslKeyExchangeAlgorithm {
			get;
		}

		/// <summary>
		/// Get the negotiated SSL or TLS key exchange algorithm strength.
		/// </summary>
		/// <remarks>
		/// Gets the negotiated SSL or TLS key exchange algorithm strength once an SSL or TLS connection has been made.
		/// </remarks>
		/// <value>The negotiated SSL or TLS key exchange algorithm strength.</value>
		public abstract int? SslKeyExchangeStrength {
			get;
		}

#if NET5_0_OR_GREATER
		/// <summary>
		/// Get the negotiated cipher suite once an SSL or TLS connection has been made.
		/// </summary>
		/// <value>The negotiated cipher suite</value>
		public abstract TlsCipherSuite? NegotiatedCipherSuite {
			get;
		} 
#endif

		/// <summary>
		/// Get whether or not the client is currently authenticated with the mail server.
		/// </summary>
		/// <remarks>
		/// <para>Gets whether or not the client is currently authenticated with the mail server.</para>
		/// <para>To authenticate with the mail server, use one of the
		/// <a href="Overload_MailKit_MailService_Authenticate.htm">Authenticate</a> methods
		/// or any of the Async alternatives.</para>
		/// </remarks>
		/// <value><c>true</c> if the client is authenticated; otherwise, <c>false</c>.</value>
		public abstract bool IsAuthenticated {
			get;
		}

		/// <summary>
		/// Gets or sets the timeout for network streaming operations, in milliseconds.
		/// </summary>
		/// <remarks>
		/// Gets or sets the underlying socket stream's <see cref="System.IO.Stream.ReadTimeout"/>
		/// and <see cref="System.IO.Stream.WriteTimeout"/> values.
		/// </remarks>
		/// <value>The timeout in milliseconds.</value>
		public abstract int Timeout {
			get; set;
		}

		const string AppleCertificateIssuer = "C=US, S=California, O=Apple Inc., CN=Apple Public Server RSA CA 12 - G1";
		const string GMailCertificateIssuer = "CN=GTS CA 1C3, O=Google Trust Services LLC, C=US";
		const string OutlookCertificateIssuer = "CN=DigiCert Cloud Services CA-1, O=DigiCert Inc, C=US";
		const string YahooCertificateIssuer = "CN=DigiCert SHA2 High Assurance Server CA, OU=www.digicert.com, O=DigiCert Inc, C=US";
		const string GmxDotComCertificateIssuer = "CN=GeoTrust RSA CA 2018, OU=www.digicert.com, O=DigiCert Inc, C=US";
		const string GmxDotNetCertificateIssuer = "CN=TeleSec ServerPass Extended Validation Class 3 CA, STREET=Untere Industriestr. 20, L=Netphen, PostalCode=57250, S=Nordrhein Westfalen, OU=T-Systems Trust Center, O=T-Systems International GmbH, C=DE";

		// Note: This method auto-generated by https://gist.github.com/jstedfast/7cd36a51cee740ed84b18435106eaea5
		internal static bool IsKnownMailServerCertificate (X509Certificate2 certificate)
		{
			var cn = certificate.GetNameInfo (X509NameType.SimpleName, false);
			var fingerprint = certificate.Thumbprint;
			var serial = certificate.SerialNumber;
			var issuer = certificate.Issuer;

			switch (cn) {
			case "imap.gmail.com":
				switch (issuer) {
				case GMailCertificateIssuer:
					return (serial == "00B93BA153E99924FA0A0000000110361E" && fingerprint == "626F8E5304A6894F73EA34C1A5BA30E659430A2F") // Expires 1/10/2022 4:47:53 AM
						|| (serial == "3D490726264C73630A0000000119533C" && fingerprint == "663B1630FCAAFBC30038BC2623F7DA087482C0FD") // Expires 1/23/2022 9:54:50 PM
						|| (serial == "2265A3434A5225380A00000001224DAF" && fingerprint == "52A853B1121A4B1A7C5DBDBFD6CE39F068888A2B") // Expires 1/31/2022 3:11:05 AM
						|| (serial == "00EC67725FAF05E6FD0A0000000125FF83" && fingerprint == "FF388B1BC174CBC3069B8709AB0CD93AF8077E94"); // Expires 2/20/2022 10:08:30 PM
				default:
					return false;
				}
			case "pop.gmail.com":
				switch (issuer) {
				case GMailCertificateIssuer:
					return (serial == "00D10ECCD5085799F50A0000000108AD82" && fingerprint == "EDC8EFF7BABFA726874E1E5753D9A203DB9FB539") // Expires 12/26/2021 9:12:18 PM
						|| (serial == "00D1539A6D091B588C0A00000001103626" && fingerprint == "F6C4E61A5F0E8E0ECB6194F13ADF6B255D9CF16D") // Expires 1/10/2022 4:48:09 AM
						|| (serial == "34C1C63D8A354B9E0A0000000119533E" && fingerprint == "081431D1D4E703B9038F630F29CE43D1505E7DA6") // Expires 1/23/2022 9:55:03 PM
						|| (serial == "32D8D7A3EB3680B80A00000001224DB8" && fingerprint == "664F07AEDFC6D1BB25669D6470CC84371F30146D") // Expires 1/31/2022 3:11:21 AM
						|| (serial == "309330098BF137760A0000000125FF88" && fingerprint == "30E95BB5541E924141CAEAB01B48FF20B308ED3C"); // Expires 2/20/2022 10:08:45 PM
				default:
					return false;
				}
			case "smtp.gmail.com":
				switch (issuer) {
				case GMailCertificateIssuer:
					return (serial == "6C04C830530304AA0A0000000110363A" && fingerprint == "28C09AAA6A21E3DDBC3DDD67FBF375AAEF61B0C9") // Expires 1/10/2022 4:49:30 AM
						|| (serial == "7A99E46AA12130370A00000001195354" && fingerprint == "57A74EA716DC96B74035A7C08CD9649FBF2D834A") // Expires 1/23/2022 9:56:15 PM
						|| (serial == "00BDF6AD1401715D6B0A00000001224DCA" && fingerprint == "4B4948C238114FC92F31C59E5B85C73D1E47BADB") // Expires 1/31/2022 3:12:48 AM
						|| (serial == "00F3402FC1B5A7247E0A0000000125FFA2" && fingerprint == "2B4D501A49992D659F9FFBA5D18D09E431BA9CEB"); // Expires 2/20/2022 10:10:08 PM
				default:
					return false;
				}
			case "outlook.com":
				switch (issuer) {
				case OutlookCertificateIssuer:
					return (serial == "0CCAC32B0EF281026392B8852AB15642" && fingerprint == "CBAA1582F1E49AD1D108193B5D38B966BE4993C6") // Expires 1/21/2022 6:59:59 PM
						|| (serial == "0CE67C905DDE83B20E77606A636AB967" && fingerprint == "E295CCF7F125F70907C2E7F97EF0F5E7D5704DE6") // Expires 10/23/2022 7:59:59 PM
						|| (serial == "010CB801C9719EE668C7A803EFD5D8C4" && fingerprint == "5223FB99040188673B9847FAF8EAC3531F0FE55B") // Expires 12/12/2022 6:59:59 PM
						|| (serial == "08349B4851225195DE03A3515F5600BE" && fingerprint == "02D13AF3D6DF147C2573AE8793AB8FBE8461E4CD"); // Expires 12/12/2022 6:59:59 PM
				default:
					return false;
				}
			case "imap.mail.me.com":
				return issuer == AppleCertificateIssuer && serial == "2EC9B6B93C77A53D15405C47A9FBC3CF" && fingerprint == "A047B6AE5E0FF51CC216C1237A44529B0A4DB0D2"; // Expires 10/2/2022 3:51:56 PM
			case "smtp.mail.me.com":
				return issuer == AppleCertificateIssuer && serial == "46A537AD83083BCCBDA20D1D8657F573" && fingerprint == "83AA1EF97EE9AC0EAD8B2C88C62C83F8EDBF2BDB"; // Expires 10/30/2022 4:11:38 PM
			case "*.imap.mail.yahoo.com":
				return issuer == YahooCertificateIssuer && serial == "07E7B4CB914FFC7FB3E03105C9DA0BE1" && fingerprint == "D7D39A265E914ADC8B443BF24DB684354D50B000"; // Expires 3/16/2022 7:59:59 PM
			case "legacy.pop.mail.yahoo.com":
				switch (issuer) {
				case YahooCertificateIssuer:
					return (serial == "09CC4977A4C14D4388D90CF6676385FE" && fingerprint == "7BA05AF724299FF0688842ADEF2837DE25F3C4FD") // Expires 12/22/2021 6:59:59 PM
						|| (serial == "03B1E9610E0E209A4EA8FC192EBF55D7" && fingerprint == "7C32F642167257B00E55A9C5DC3E35F1719193BD"); // Expires 5/18/2022 11:59:59 PM
				default:
					return false;
				}
			case "smtp.mail.yahoo.com":
				return issuer == YahooCertificateIssuer && serial == "096122E949C73D57587E904DE8EBE2BC" && fingerprint == "C38CA2874F6489686FAE148482325EC3D8763D81"; // Expires 4/13/2022 7:59:59 PM
			case "mout.gmx.com":
				return issuer == GmxDotComCertificateIssuer && serial == "06206F2270494CD7AD11F2B17E286C2C" && fingerprint == "A7D3BCC363B307EC3BDE21269A2F05117D6614A8"; // Expires 7/12/2022 8:00:00 AM
			case "mail.gmx.com":
				return issuer == GmxDotComCertificateIssuer && serial == "0719A4D33A18B550133DDA3253AF6C96" && fingerprint == "948B0C3FA22BC12C91EEE5B1631A6C41B4A01B9C"; // Expires 7/12/2022 8:00:00 AM
			case "mail.gmx.net":
				return issuer == GmxDotNetCertificateIssuer && serial == "070E7CD59BB7AFD73E8A206219C4F011" && fingerprint == "E66DC8FE17C9A7718D17441CBE347D1D6F7BF3D2"; // Expires 5/3/2022 7:59:59 PM
			default:
				return false;
			}
		}

		static bool IsUntrustedRoot (X509Chain chain)
		{
			foreach (var status in chain.ChainStatus) {
				if (status.Status == X509ChainStatusFlags.NoError || status.Status == X509ChainStatusFlags.UntrustedRoot)
					continue;

				return false;
			}

			return true;
		}

		/// <summary>
		/// The default server certificate validation callback used when connecting via SSL or TLS.
		/// </summary>
		/// <remarks>
		/// <para>The default server certificate validation callback recognizes and accepts the certificates
		/// for a list of commonly used mail servers such as gmail.com, outlook.com, mail.me.com, yahoo.com,
		/// and gmx.net.</para>
		/// </remarks>
		/// <returns><c>true</c> if the certificate is deemed valid; otherwise, <c>false</c>.</returns>
		/// <param name="sender">The object that is connecting via SSL or TLS.</param>
		/// <param name="certificate">The server's SSL certificate.</param>
		/// <param name="chain">The server's SSL certificate chain.</param>
		/// <param name="sslPolicyErrors">The SSL policy errors.</param>
		protected static bool DefaultServerCertificateValidationCallback (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
		{
			const SslPolicyErrors mask = SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch;

			if (sslPolicyErrors == SslPolicyErrors.None)
				return true;

			if ((sslPolicyErrors & mask) == 0) {
				// At this point, all that is left is SslPolicyErrors.RemoteCertificateChainErrors

				// If the problem is an untrusted root, then compare the certificate to a list of known mail server certificates.
				if (IsUntrustedRoot (chain) && certificate is X509Certificate2 certificate2) {
					if (IsKnownMailServerCertificate (certificate2))
						return true;
				}
			}

			return false;
		}

#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
		/// <summary>
		/// Gets the SSL/TLS client authentication options for use with .NET5's SslStream.AuthenticateAsClient() API.
		/// </summary>
		/// <remarks>
		/// Gets the SSL/TLS client authentication options for use with .NET5's SslStream.AuthenticateAsClient() API.
		/// </remarks>
		/// <param name="host">The target host that the client is connected to.</param>
		/// <param name="remoteCertificateValidationCallback">The remote certificate validation callback.</param>
		/// <returns>The client SSL/TLS authentication options.</returns>
		protected SslClientAuthenticationOptions GetSslClientAuthenticationOptions (string host, RemoteCertificateValidationCallback remoteCertificateValidationCallback)
		{
			return new SslClientAuthenticationOptions {
				CertificateRevocationCheckMode = CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
				ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol (Protocol) },
				RemoteCertificateValidationCallback = remoteCertificateValidationCallback,
#if NET5_0_OR_GREATER
				CipherSuitesPolicy = SslCipherSuitesPolicy,
#endif
				ClientCertificates = ClientCertificates,
				EnabledSslProtocols = SslProtocols,
				TargetHost = host
			};
		}
#endif

		internal async Task<Stream> ConnectNetwork (string host, int port, bool doAsync, CancellationToken cancellationToken)
		{
			if (ProxyClient != null) {
				ProxyClient.LocalEndPoint = LocalEndPoint;

				if (doAsync)
					return await ProxyClient.ConnectAsync (host, port, Timeout, cancellationToken).ConfigureAwait (false);

				return ProxyClient.Connect (host, port, Timeout, cancellationToken);
			}

			var socket = await SocketUtils.ConnectAsync (host, port, LocalEndPoint, Timeout, doAsync, cancellationToken).ConfigureAwait (false);

			return new NetworkStream (socket, true);
		}

		/// <summary>
		/// Establish a connection to the specified mail server.
		/// </summary>
		/// <remarks>
		/// Establishes a connection to the specified mail server.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\SmtpExamples.cs" region="SendMessage"/>
		/// </example>
		/// <param name="host">The host name to connect to.</param>
		/// <param name="port">The port to connect to. If the specified port is <c>0</c>, then the default port will be used.</param>
		/// <param name="options">The secure socket options to when connecting.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="host"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is not between <c>0</c> and <c>65535</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The <paramref name="host"/> is a zero-length string.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is already connected.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.Net.Sockets.SocketException">
		/// A socket error occurred trying to connect to the remote host.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public abstract void Connect (string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Asynchronously establish a connection to the specified mail server.
		/// </summary>
		/// <remarks>
		/// Asynchronously establishes a connection to the specified mail server.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="host">The host name to connect to.</param>
		/// <param name="port">The port to connect to. If the specified port is <c>0</c>, then the default port will be used.</param>
		/// <param name="options">The secure socket options to when connecting.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="host"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is not between <c>0</c> and <c>65535</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The <paramref name="host"/> is a zero-length string.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is already connected.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.Net.Sockets.SocketException">
		/// A socket error occurred trying to connect to the remote host.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public abstract Task ConnectAsync (string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Establish a connection to the specified mail server using the provided socket.
		/// </summary>
		/// <remarks>
		/// <para>Establish a connection to the specified mail server using the provided socket.</para>
		/// <para>If a successful connection is made, the <see cref="AuthenticationMechanisms"/>
		/// property will be populated.</para>
		/// </remarks>
		/// <param name="socket">The socket to use for the connection.</param>
		/// <param name="host">The host name to connect to.</param>
		/// <param name="port">The port to connect to. If the specified port is <c>0</c>, then the default port will be used.</param>
		/// <param name="options">The secure socket options to when connecting.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="socket"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="host"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is not between <c>0</c> and <c>65535</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="socket"/> is not connected.</para>
		/// <para>-or-</para>
		/// <para>The <paramref name="host"/> is a zero-length string.</para>
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="IMailService"/> is already connected.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command was rejected by the mail server.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server responded with an unexpected token.
		/// </exception>
		public abstract void Connect (Socket socket, string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Asynchronously establish a connection to the specified mail server using the provided socket.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously establishes a connection to the specified mail server using the provided socket.</para>
		/// <para>If a successful connection is made, the <see cref="AuthenticationMechanisms"/>
		/// property will be populated.</para>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="socket">The socket to use for the connection.</param>
		/// <param name="host">The host name to connect to.</param>
		/// <param name="port">The port to connect to. If the specified port is <c>0</c>, then the default port will be used.</param>
		/// <param name="options">The secure socket options to when connecting.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="socket"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="host"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is not between <c>0</c> and <c>65535</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="socket"/> is not connected.</para>
		/// <para>-or-</para>
		/// <para>The <paramref name="host"/> is a zero-length string.</para>
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="IMailService"/> is already connected.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command was rejected by the mail server.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server responded with an unexpected token.
		/// </exception>
		public abstract Task ConnectAsync (Socket socket, string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Establish a connection to the specified mail server using the provided stream.
		/// </summary>
		/// <remarks>
		/// <para>Establish a connection to the specified mail server using the provided stream.</para>
		/// <para>If a successful connection is made, the <see cref="AuthenticationMechanisms"/>
		/// property will be populated.</para>
		/// </remarks>
		/// <param name="stream">The stream to use for the connection.</param>
		/// <param name="host">The host name to connect to.</param>
		/// <param name="port">The port to connect to. If the specified port is <c>0</c>, then the default port will be used.</param>
		/// <param name="options">The secure socket options to when connecting.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="stream"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="host"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is not between <c>0</c> and <c>65535</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The <paramref name="host"/> is a zero-length string.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="IMailService"/> is already connected.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command was rejected by the mail server.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server responded with an unexpected token.
		/// </exception>
		public abstract void Connect (Stream stream, string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Asynchronously establish a connection to the specified mail server using the provided stream.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously establishes a connection to the specified mail server using the provided stream.</para>
		/// <para>If a successful connection is made, the <see cref="AuthenticationMechanisms"/>
		/// property will be populated.</para>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="stream">The stream to use for the connection.</param>
		/// <param name="host">The host name to connect to.</param>
		/// <param name="port">The port to connect to. If the specified port is <c>0</c>, then the default port will be used.</param>
		/// <param name="options">The secure socket options to when connecting.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="stream"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="host"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is not between <c>0</c> and <c>65535</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The <paramref name="host"/> is a zero-length string.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="IMailService"/> is already connected.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command was rejected by the mail server.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server responded with an unexpected token.
		/// </exception>
		public abstract Task ConnectAsync (Stream stream, string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default (CancellationToken));

		internal SecureSocketOptions GetSecureSocketOptions (Uri uri)
		{
			var query = uri.ParsedQuery ();
			var protocol = uri.Scheme;

			// Note: early versions of MailKit used "pop3" and "pop3s"
			if (protocol.Equals ("pop3s", StringComparison.OrdinalIgnoreCase))
				protocol = "pops";
			else if (protocol.Equals ("pop3", StringComparison.OrdinalIgnoreCase))
				protocol = "pop";

			if (protocol.Equals (Protocol + "s", StringComparison.OrdinalIgnoreCase))
				return SecureSocketOptions.SslOnConnect;

			if (!protocol.Equals (Protocol, StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException ("Unknown URI scheme.", nameof (uri));

			if (query.TryGetValue ("starttls", out string value)) {
				switch (value.ToLowerInvariant ()) {
				default:
					return SecureSocketOptions.StartTlsWhenAvailable;
				case "always": case "true": case "yes":
					return SecureSocketOptions.StartTls;
				case "never": case "false": case "no":
					return SecureSocketOptions.None;
				}
			}

			return SecureSocketOptions.StartTlsWhenAvailable;
		}

		/// <summary>
		/// Establish a connection to the specified mail server.
		/// </summary>
		/// <remarks>
		/// Establishes a connection to the specified mail server.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\SmtpExamples.cs" region="SendMessageUri"/>
		/// </example>
		/// <param name="uri">The server URI.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// The <paramref name="uri"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The <paramref name="uri"/> is not an absolute URI.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is already connected.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.Net.Sockets.SocketException">
		/// A socket error occurred trying to connect to the remote host.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public void Connect (Uri uri, CancellationToken cancellationToken = default (CancellationToken))
		{
			if (uri == null)
				throw new ArgumentNullException (nameof (uri));

			if (!uri.IsAbsoluteUri)
				throw new ArgumentException ("The uri must be absolute.", nameof (uri));

			var options = GetSecureSocketOptions (uri);

			Connect (uri.Host, uri.Port < 0 ? 0 : uri.Port, options, cancellationToken);
		}

		/// <summary>
		/// Asynchronously establish a connection to the specified mail server.
		/// </summary>
		/// <remarks>
		/// Asynchronously establishes a connection to the specified mail server.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="uri">The server URI.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// The <paramref name="uri"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The <paramref name="uri"/> is not an absolute URI.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is already connected.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.Net.Sockets.SocketException">
		/// A socket error occurred trying to connect to the remote host.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public Task ConnectAsync (Uri uri, CancellationToken cancellationToken = default (CancellationToken))
		{
			if (uri == null)
				throw new ArgumentNullException (nameof (uri));

			if (!uri.IsAbsoluteUri)
				throw new ArgumentException ("The uri must be absolute.", nameof (uri));

			var options = GetSecureSocketOptions (uri);

			return ConnectAsync (uri.Host, uri.Port < 0 ? 0 : uri.Port, options, cancellationToken);
		}

		/// <summary>
		/// Establish a connection to the specified mail server.
		/// </summary>
		/// <remarks>
		/// <para>Establishes a connection to the specified mail server.</para>
		/// <note type="note">
		/// <para>The <paramref name="useSsl"/> argument only controls whether or
		/// not the client makes an SSL-wrapped connection. In other words, even if the
		/// <paramref name="useSsl"/> parameter is <c>false</c>, SSL/TLS may still be used if
		/// the mail server supports the STARTTLS extension.</para>
		/// <para>To disable all use of SSL/TLS, use the
		/// <see cref="Connect(string,int,MailKit.Security.SecureSocketOptions,System.Threading.CancellationToken)"/>
		/// overload with a value of
		/// <see cref="MailKit.Security.SecureSocketOptions.None">SecureSocketOptions.None</see>
		/// instead.</para>
		/// </note>
		/// </remarks>
		/// <param name="host">The host to connect to.</param>
		/// <param name="port">The port to connect to. If the specified port is <c>0</c>, then the default port will be used.</param>
		/// <param name="useSsl"><value>true</value> if the client should make an SSL-wrapped connection to the server; otherwise, <value>false</value>.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// The <paramref name="host"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is out of range (<value>0</value> to <value>65535</value>, inclusive).
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The <paramref name="host"/> is a zero-length string.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is already connected.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.Net.Sockets.SocketException">
		/// A socket error occurred trying to connect to the remote host.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public void Connect (string host, int port, bool useSsl, CancellationToken cancellationToken = default (CancellationToken))
		{
			if (host == null)
				throw new ArgumentNullException (nameof (host));

			if (host.Length == 0)
				throw new ArgumentException ("The host name cannot be empty.", nameof (host));

			if (port < 0 || port > 65535)
				throw new ArgumentOutOfRangeException (nameof (port));

			Connect (host, port, useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
		}

		/// <summary>
		/// Asynchronously establish a connection to the specified mail server.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously establishes a connection to the specified mail server.</para>
		/// <note type="note">
		/// <para>The <paramref name="useSsl"/> argument only controls whether or
		/// not the client makes an SSL-wrapped connection. In other words, even if the
		/// <paramref name="useSsl"/> parameter is <c>false</c>, SSL/TLS may still be used if
		/// the mail server supports the STARTTLS extension.</para>
		/// <para>To disable all use of SSL/TLS, use the
		/// <see cref="ConnectAsync(string,int,MailKit.Security.SecureSocketOptions,System.Threading.CancellationToken)"/>
		/// overload with a value of
		/// <see cref="MailKit.Security.SecureSocketOptions.None">SecureSocketOptions.None</see>
		/// instead.</para>
		/// </note>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="host">The host to connect to.</param>
		/// <param name="port">The port to connect to. If the specified port is <c>0</c>, then the default port will be used.</param>
		/// <param name="useSsl"><value>true</value> if the client should make an SSL-wrapped connection to the server; otherwise, <value>false</value>.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// The <paramref name="host"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="port"/> is out of range (<value>0</value> to <value>65535</value>, inclusive).
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The <paramref name="host"/> is a zero-length string.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is already connected.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.Net.Sockets.SocketException">
		/// A socket error occurred trying to connect to the remote host.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public Task ConnectAsync (string host, int port, bool useSsl, CancellationToken cancellationToken = default (CancellationToken))
		{
			if (host == null)
				throw new ArgumentNullException (nameof (host));

			if (host.Length == 0)
				throw new ArgumentException ("The host name cannot be empty.", nameof (host));

			if (port < 0 || port > 65535)
				throw new ArgumentOutOfRangeException (nameof (port));

			return ConnectAsync (host, port, useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
		}

		/// <summary>
		/// Authenticate using the supplied credentials.
		/// </summary>
		/// <remarks>
		/// <para>If the server supports one or more SASL authentication mechanisms,
		/// then the SASL mechanisms that both the client and server support are tried
		/// in order of greatest security to weakest security. Once a SASL
		/// authentication mechanism is found that both client and server support,
		/// the credentials are used to authenticate.</para>
		/// <para>If the server does not support SASL or if no common SASL mechanisms
		/// can be found, then the default login command is used as a fallback.</para>
		/// <note type="tip">To prevent the usage of certain authentication mechanisms,
		/// simply remove them from the <see cref="AuthenticationMechanisms"/> hash set
		/// before calling this method.</note>
		/// </remarks>
		/// <param name="encoding">The encoding to use for the user's credentials.</param>
		/// <param name="credentials">The user's credentials.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="encoding"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="credentials"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is not connected or is already authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="MailKit.Security.AuthenticationException">
		/// Authentication using the supplied credentials has failed.
		/// </exception>
		/// <exception cref="MailKit.Security.SaslException">
		/// A SASL authentication error occurred.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public abstract void Authenticate (Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Asynchronously authenticate using the supplied credentials.
		/// </summary>
		/// <remarks>
		/// <para>If the server supports one or more SASL authentication mechanisms,
		/// then the SASL mechanisms that both the client and server support are tried
		/// in order of greatest security to weakest security. Once a SASL
		/// authentication mechanism is found that both client and server support,
		/// the credentials are used to authenticate.</para>
		/// <para>If the server does not support SASL or if no common SASL mechanisms
		/// can be found, then the default login command is used as a fallback.</para>
		/// <note type="tip">To prevent the usage of certain authentication mechanisms,
		/// simply remove them from the <see cref="AuthenticationMechanisms"/> hash set
		/// before calling this method.</note>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="encoding">The encoding to use for the user's credentials.</param>
		/// <param name="credentials">The user's credentials.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="encoding"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="credentials"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is not connected or is already authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="MailKit.Security.AuthenticationException">
		/// Authentication using the supplied credentials has failed.
		/// </exception>
		/// <exception cref="MailKit.Security.SaslException">
		/// A SASL authentication error occurred.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public abstract Task AuthenticateAsync (Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Authenticate using the supplied credentials.
		/// </summary>
		/// <remarks>
		/// <para>If the server supports one or more SASL authentication mechanisms,
		/// then the SASL mechanisms that both the client and server support are tried
		/// in order of greatest security to weakest security. Once a SASL
		/// authentication mechanism is found that both client and server support,
		/// the credentials are used to authenticate.</para>
		/// <para>If the server does not support SASL or if no common SASL mechanisms
		/// can be found, then the default login command is used as a fallback.</para>
		/// <note type="tip">To prevent the usage of certain authentication mechanisms,
		/// simply remove them from the <see cref="AuthenticationMechanisms"/> hash set
		/// before calling this method.</note>
		/// </remarks>
		/// <param name="credentials">The user's credentials.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="credentials"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is not connected or is already authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="MailKit.Security.AuthenticationException">
		/// Authentication using the supplied credentials has failed.
		/// </exception>
		/// <exception cref="MailKit.Security.SaslException">
		/// A SASL authentication error occurred.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public void Authenticate (ICredentials credentials, CancellationToken cancellationToken = default (CancellationToken))
		{
			Authenticate (Encoding.UTF8, credentials, cancellationToken);
		}

		/// <summary>
		/// Asynchronously authenticate using the supplied credentials.
		/// </summary>
		/// <remarks>
		/// <para>If the server supports one or more SASL authentication mechanisms,
		/// then the SASL mechanisms that both the client and server support are tried
		/// in order of greatest security to weakest security. Once a SASL
		/// authentication mechanism is found that both client and server support,
		/// the credentials are used to authenticate.</para>
		/// <para>If the server does not support SASL or if no common SASL mechanisms
		/// can be found, then the default login command is used as a fallback.</para>
		/// <note type="tip">To prevent the usage of certain authentication mechanisms,
		/// simply remove them from the <see cref="AuthenticationMechanisms"/> hash set
		/// before calling this method.</note>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="credentials">The user's credentials.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="credentials"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is not connected or is already authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="MailKit.Security.AuthenticationException">
		/// Authentication using the supplied credentials has failed.
		/// </exception>
		/// <exception cref="MailKit.Security.SaslException">
		/// A SASL authentication error occurred.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public Task AuthenticateAsync (ICredentials credentials, CancellationToken cancellationToken = default (CancellationToken))
		{
			return AuthenticateAsync (Encoding.UTF8, credentials, cancellationToken);
		}

		/// <summary>
		/// Authenticate using the specified user name and password.
		/// </summary>
		/// <remarks>
		/// <para>If the server supports one or more SASL authentication mechanisms,
		/// then the SASL mechanisms that both the client and server support are tried
		/// in order of greatest security to weakest security. Once a SASL
		/// authentication mechanism is found that both client and server support,
		/// the credentials are used to authenticate.</para>
		/// <para>If the server does not support SASL or if no common SASL mechanisms
		/// can be found, then the default login command is used as a fallback.</para>
		/// <note type="tip">To prevent the usage of certain authentication mechanisms,
		/// simply remove them from the <see cref="AuthenticationMechanisms"/> hash set
		/// before calling this method.</note>
		/// </remarks>
		/// <param name="encoding">The encoding to use for the user's credentials.</param>
		/// <param name="userName">The user name.</param>
		/// <param name="password">The password.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="encoding"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="userName"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="password"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is not connected or is already authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="MailKit.Security.AuthenticationException">
		/// Authentication using the supplied credentials has failed.
		/// </exception>
		/// <exception cref="MailKit.Security.SaslException">
		/// A SASL authentication error occurred.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public void Authenticate (Encoding encoding, string userName, string password, CancellationToken cancellationToken = default (CancellationToken))
		{
			if (encoding == null)
				throw new ArgumentNullException (nameof (encoding));

			if (userName == null)
				throw new ArgumentNullException (nameof (userName));

			if (password == null)
				throw new ArgumentNullException (nameof (password));

			var credentials = new NetworkCredential (userName, password);

			Authenticate (encoding, credentials, cancellationToken);
		}

		/// <summary>
		/// Asynchronously authenticate using the specified user name and password.
		/// </summary>
		/// <remarks>
		/// <para>If the server supports one or more SASL authentication mechanisms,
		/// then the SASL mechanisms that both the client and server support are tried
		/// in order of greatest security to weakest security. Once a SASL
		/// authentication mechanism is found that both client and server support,
		/// the credentials are used to authenticate.</para>
		/// <para>If the server does not support SASL or if no common SASL mechanisms
		/// can be found, then the default login command is used as a fallback.</para>
		/// <note type="tip">To prevent the usage of certain authentication mechanisms,
		/// simply remove them from the <see cref="AuthenticationMechanisms"/> hash set
		/// before calling this method.</note>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="encoding">The encoding to use for the user's credentials.</param>
		/// <param name="userName">The user name.</param>
		/// <param name="password">The password.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="encoding"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="userName"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="password"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is not connected or is already authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="MailKit.Security.AuthenticationException">
		/// Authentication using the supplied credentials has failed.
		/// </exception>
		/// <exception cref="MailKit.Security.SaslException">
		/// A SASL authentication error occurred.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public Task AuthenticateAsync (Encoding encoding, string userName, string password, CancellationToken cancellationToken = default (CancellationToken))
		{
			if (encoding == null)
				throw new ArgumentNullException (nameof (encoding));

			if (userName == null)
				throw new ArgumentNullException (nameof (userName));

			if (password == null)
				throw new ArgumentNullException (nameof (password));

			var credentials = new NetworkCredential (userName, password);

			return AuthenticateAsync (encoding, credentials, cancellationToken);
		}

		/// <summary>
		/// Authenticate using the specified user name and password.
		/// </summary>
		/// <remarks>
		/// <para>If the server supports one or more SASL authentication mechanisms,
		/// then the SASL mechanisms that both the client and server support are tried
		/// in order of greatest security to weakest security. Once a SASL
		/// authentication mechanism is found that both client and server support,
		/// the credentials are used to authenticate.</para>
		/// <para>If the server does not support SASL or if no common SASL mechanisms
		/// can be found, then the default login command is used as a fallback.</para>
		/// <note type="tip">To prevent the usage of certain authentication mechanisms,
		/// simply remove them from the <see cref="AuthenticationMechanisms"/> hash set
		/// before calling this method.</note>
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\SmtpExamples.cs" region="SendMessage"/>
		/// </example>
		/// <param name="userName">The user name.</param>
		/// <param name="password">The password.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="userName"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="password"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is not connected or is already authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="MailKit.Security.AuthenticationException">
		/// Authentication using the supplied credentials has failed.
		/// </exception>
		/// <exception cref="MailKit.Security.SaslException">
		/// A SASL authentication error occurred.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public void Authenticate (string userName, string password, CancellationToken cancellationToken = default (CancellationToken))
		{
			Authenticate (Encoding.UTF8, userName, password, cancellationToken);
		}

		/// <summary>
		/// Asynchronously authenticate using the specified user name and password.
		/// </summary>
		/// <remarks>
		/// <para>If the server supports one or more SASL authentication mechanisms,
		/// then the SASL mechanisms that both the client and server support are tried
		/// in order of greatest security to weakest security. Once a SASL
		/// authentication mechanism is found that both client and server support,
		/// the credentials are used to authenticate.</para>
		/// <para>If the server does not support SASL or if no common SASL mechanisms
		/// can be found, then the default login command is used as a fallback.</para>
		/// <note type="tip">To prevent the usage of certain authentication mechanisms,
		/// simply remove them from the <see cref="AuthenticationMechanisms"/> hash set
		/// before calling this method.</note>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="userName">The user name.</param>
		/// <param name="password">The password.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="userName"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="password"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is not connected or is already authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="MailKit.Security.AuthenticationException">
		/// Authentication using the supplied credentials has failed.
		/// </exception>
		/// <exception cref="MailKit.Security.SaslException">
		/// A SASL authentication error occurred.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public Task AuthenticateAsync (string userName, string password, CancellationToken cancellationToken = default (CancellationToken))
		{
			return AuthenticateAsync (Encoding.UTF8, userName, password, cancellationToken);
		}

		/// <summary>
		/// Authenticate using the specified SASL mechanism.
		/// </summary>
		/// <remarks>
		/// <para>Authenticates using the specified SASL mechanism.</para>
		/// <para>For a list of available SASL authentication mechanisms supported by the server,
		/// check the <see cref="AuthenticationMechanisms"/> property after the service has been
		/// connected.</para>
		/// </remarks>
		/// <param name="mechanism">The SASL mechanism.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="mechanism"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is not connected or is already authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="MailKit.Security.AuthenticationException">
		/// Authentication using the supplied credentials has failed.
		/// </exception>
		/// <exception cref="MailKit.Security.SaslException">
		/// A SASL authentication error occurred.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public abstract void Authenticate (SaslMechanism mechanism, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Asynchronously authenticate using the specified SASL mechanism.
		/// </summary>
		/// <remarks>
		/// <para>Authenticates using the specified SASL mechanism.</para>
		/// <para>For a list of available SASL authentication mechanisms supported by the server,
		/// check the <see cref="AuthenticationMechanisms"/> property after the service has been
		/// connected.</para>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="mechanism">The SASL mechanism.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="mechanism"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="MailService"/> is not connected or is already authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="MailKit.Security.AuthenticationException">
		/// Authentication using the supplied credentials has failed.
		/// </exception>
		/// <exception cref="MailKit.Security.SaslException">
		/// A SASL authentication error occurred.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// A protocol error occurred.
		/// </exception>
		public abstract Task AuthenticateAsync (SaslMechanism mechanism, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Disconnect the service.
		/// </summary>
		/// <remarks>
		/// If <paramref name="quit"/> is <c>true</c>, a logout/quit command will be issued in order to disconnect cleanly.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\SmtpExamples.cs" region="SendMessage"/>
		/// </example>
		/// <param name="quit">If set to <c>true</c>, a logout/quit command will be issued in order to disconnect cleanly.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		public abstract void Disconnect (bool quit, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Asynchronously disconnect the service.
		/// </summary>
		/// <remarks>
		/// If <paramref name="quit"/> is <c>true</c>, a logout/quit command will be issued in order to disconnect cleanly.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="quit">If set to <c>true</c>, a logout/quit command will be issued in order to disconnect cleanly.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		public abstract Task DisconnectAsync (bool quit, CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Ping the mail server to keep the connection alive.
		/// </summary>
		/// <remarks>
		/// Mail servers, if left idle for too long, will automatically drop the connection.
		/// </remarks>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// <para>The <see cref="MailService"/> is not connected.</para>
		/// <para>-or-</para>
		/// <para>The <see cref="MailService"/> is not authenticated.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command was rejected by the mail server.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server responded with an unexpected token.
		/// </exception>
		public abstract void NoOp (CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Asynchronously ping the mail server to keep the connection alive.
		/// </summary>
		/// <remarks>
		/// Mail servers, if left idle for too long, will automatically drop the connection.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="MailService"/> has been disposed.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// <para>The <see cref="MailService"/> is not connected.</para>
		/// <para>-or-</para>
		/// <para>The <see cref="MailService"/> is not authenticated.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command was rejected by the mail server.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server responded with an unexpected token.
		/// </exception>
		public abstract Task NoOpAsync (CancellationToken cancellationToken = default (CancellationToken));

		/// <summary>
		/// Occurs when the client has been successfully connected.
		/// </summary>
		/// <remarks>
		/// The <see cref="Connected"/> event is raised when the client
		/// successfully connects to the mail server.
		/// </remarks>
		public event EventHandler<ConnectedEventArgs> Connected;

		/// <summary>
		/// Raise the connected event.
		/// </summary>
		/// <remarks>
		/// Raises the connected event.
		/// </remarks>
		/// <param name="host">The name of the host that the client connected to.</param>
		/// <param name="port">The port that the client connected to on the remote host.</param>
		/// <param name="options">The SSL/TLS options that were used when connecting.</param>
		protected virtual void OnConnected (string host, int port, SecureSocketOptions options)
		{
			Connected?.Invoke (this, new ConnectedEventArgs (host, port, options));
		}

		/// <summary>
		/// Occurs when the client gets disconnected.
		/// </summary>
		/// <remarks>
		/// The <see cref="Disconnected"/> event is raised whenever the client
		/// gets disconnected.
		/// </remarks>
		public event EventHandler<DisconnectedEventArgs> Disconnected;

		/// <summary>
		/// Raise the disconnected event.
		/// </summary>
		/// <remarks>
		/// Raises the disconnected event.
		/// </remarks>
		/// <param name="host">The name of the host that the client was connected to.</param>
		/// <param name="port">The port that the client was connected to on the remote host.</param>
		/// <param name="options">The SSL/TLS options that were used by the client.</param>
		/// <param name="requested"><c>true</c> if the disconnect was explicitly requested; otherwise, <c>false</c>.</param>
		protected virtual void OnDisconnected (string host, int port, SecureSocketOptions options, bool requested)
		{
			Disconnected?.Invoke (this, new DisconnectedEventArgs (host, port, options, requested));
		}

		/// <summary>
		/// Occurs when the client has been successfully authenticated.
		/// </summary>
		/// <remarks>
		/// The <see cref="Authenticated"/> event is raised whenever the client
		/// has been authenticated.
		/// </remarks>
		public event EventHandler<AuthenticatedEventArgs> Authenticated;

		/// <summary>
		/// Raise the authenticated event.
		/// </summary>
		/// <remarks>
		/// Raises the authenticated event.
		/// </remarks>
		/// <param name="message">The notification sent by the server when the client successfully authenticates.</param>
		protected virtual void OnAuthenticated (string message)
		{
			Authenticated?.Invoke (this, new AuthenticatedEventArgs (message));
		}

		/// <summary>
		/// Releases the unmanaged resources used by the <see cref="MailService"/> and
		/// optionally releases the managed resources.
		/// </summary>
		/// <remarks>
		/// Releases the unmanaged resources used by the <see cref="MailService"/> and
		/// optionally releases the managed resources.
		/// </remarks>
		/// <param name="disposing"><c>true</c> to release both managed and unmanaged resources;
		/// <c>false</c> to release only the unmanaged resources.</param>
		protected virtual void Dispose (bool disposing)
		{
			if (disposing)
				ProtocolLogger.Dispose ();
		}

		/// <summary>
		/// Releases all resource used by the <see cref="MailService"/> object.
		/// </summary>
		/// <remarks>Call <see cref="Dispose()"/> when you are finished using the <see cref="MailService"/>. The
		/// <see cref="Dispose()"/> method leaves the <see cref="MailService"/> in an unusable state. After
		/// calling <see cref="Dispose()"/>, you must release all references to the <see cref="MailService"/> so
		/// the garbage collector can reclaim the memory that the <see cref="MailService"/> was occupying.</remarks>
		public void Dispose ()
		{
			Dispose (true);
			GC.SuppressFinalize (this);
		}
	}
}
