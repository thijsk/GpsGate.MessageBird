using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using Franson.Directory;
using Franson.Message;
using Franson.Message.Sms;
using Franson.Reflection;
using GpsGate.Online.Net;
using NLog;

namespace GpsGate.MessageBird
{
	[Loadable(Installable=false, Description="GpsGate HTTP URL Message Provider.")]
	public class MessageBirdProvider : QueuedMessageProvider
	{
		private Logger m_nlog = LogManager.GetCurrentClassLogger();

		private HttpListener m_httpListener = null;

		protected override GpsGate.Online.Net.QueueSendMode QueueSendMode
		{
			get
			{
				return GpsGate.Online.Net.QueueSendMode.ForceAsync;
			}
		}

		public MessageBirdProvider()
		{
		}

		protected override void DoStart()
		{
			Exception exception;
			base.DoStart();
			base.RegisterOutgoingQueue(new MessageQueue(this, true, false, this));
			this.SetIncomingQueue(new MessageQueue(this, false, true, this));
			try
			{
				this.m_httpListener = new HttpListener();
				string item = ConfigurationManager.AppSettings["ServerAddress"];
				string str = null;
				if (item == null)
				{
					str = string.Concat("http://*:8008/MessageBird/", (string.IsNullOrEmpty(base.RouteLabel) ? "" : string.Concat(base.RouteLabel, "/")));
					this.m_nlog.Info("HTTP: {0} {1}", str, base.GetType());
					this.m_httpListener.Prefixes.Add(str);
				}
				else
				{
					SiteSettings siteSetting = new SiteSettings();
					siteSetting.LoadByNamespace(Settings.GpsGateServerNamespace);
					string value = siteSetting.GetValue("ServerHostname", typeof(string)) as string;
					if (value != null)
					{
						str = string.Concat("http://", value, ":8008/MessageBird/", (string.IsNullOrEmpty(base.RouteLabel) ? "" : string.Concat(base.RouteLabel, "/")));
						this.m_nlog.Info("HTTP: {0} {1}", str, base.GetType());
						try
						{
							this.m_httpListener.Prefixes.Add(str);
						}
						catch (Exception exception1)
						{
							exception = exception1;
							this.m_nlog.ErrorException(exception.GetType().ToString(), exception);
						}
					}
					if (item != value)
					{
						str = string.Concat("http://", item, ":8008/MessageBird/", (string.IsNullOrEmpty(base.RouteLabel) ? "" : string.Concat(base.RouteLabel, "/")));
						this.m_nlog.Info("HTTP: {0} {1}", str, base.GetType());
						try
						{
							this.m_httpListener.Prefixes.Add(str);
						}
						catch (Exception exception2)
						{
							exception = exception2;
							this.m_nlog.ErrorException(exception.GetType().ToString(), exception);
						}
					}
				}
				this.m_httpListener.Start();
				this.m_httpListener.BeginGetContext(new AsyncCallback(this.m_NewRequest), this.m_httpListener);
			}
			catch (Exception exception3)
			{
				exception = exception3;
				this.m_nlog.ErrorException(exception.GetType().ToString(), exception);
				throw;
			}
		}

		protected override void DoStop()
		{
			this.m_httpListener.Stop();
			base.DoStop();
		}

		private void m_NewRequest(IAsyncResult result)
		{
			Exception exception;
			try
			{
				HttpListener asyncState = (HttpListener)result.AsyncState;
				HttpListenerContext httpListenerContext = this.m_httpListener.EndGetContext(result);
				this.m_nlog.Info("New HTTP Message from {0} type {1}", httpListenerContext.Request.RemoteEndPoint, base.GetType());
				try
				{
					try
					{
						this.m_nlog.Info("HTTP GET IN: {0}", httpListenerContext.Request.RawUrl);
						this.m_httpListener.BeginGetContext(new AsyncCallback(this.m_NewRequest), this.m_httpListener);
						DateTime utcNow = DateTime.UtcNow;
						string item = httpListenerContext.Request.QueryString["message"];
						string str = string.Concat("+", httpListenerContext.Request.QueryString["sender"]);
						SmsMessage smsMessage = new SmsMessage(item, MSISDN.Parse(str), utcNow);
						this.OnMessageReceived(smsMessage);
					}
					catch (Exception exception1)
					{
						exception = exception1;
						this.m_nlog.Error("Could not process: {0}", httpListenerContext.Request.Url.Query);
						this.m_nlog.ErrorException(exception.GetType().ToString(), exception);
						httpListenerContext.Response.StatusCode = 500;
					}
				}
				finally
				{
					httpListenerContext.Response.Close();
				}
			}
			catch (Exception exception2)
			{
				exception = exception2;
				if (base.IsStarted)
				{
					this.m_nlog.ErrorException(exception.GetType().ToString(), exception);
				}
			}
		}

		protected override bool SendOutgoing(ProviderMessage provMsg)
		{
			bool statusCode = false;
			string url = base.Url;
			if (string.IsNullOrEmpty(url))
			{
				throw new ArgumentException("No URL specified for HTTP message provider");
			}
			if (url.IndexOf("://") == -1)
			{
				url = string.Concat("http://", url);
			}
			string message = provMsg.Message;
			if ((message == null ? false : message.Length > 160))
			{
				message = message.Substring(0, 160);
			}
			StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("&destination=");
            stringBuilder.Append(HttpUtility.UrlEncode(provMsg.ClientAddress.Replace("+", "")));
            stringBuilder.Append("&body=");
			stringBuilder.Append(HttpUtility.UrlEncode(message, Encoding.GetEncoding(1252)));
			url = string.Concat(url, stringBuilder.ToString());
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
			httpWebRequest.Method = "GET";
			this.m_nlog.Info("Send: {0}", url);
			string end = null;
			try
			{
				HttpWebResponse response = (HttpWebResponse)httpWebRequest.GetResponse();
				end = (new StreamReader(response.GetResponseStream())).ReadToEnd();
				
				statusCode = HttpStatusCode.OK == response.StatusCode;
				response.Close();
			}
			catch (Exception exception2)
			{
				Exception exception1 = exception2;
				this.m_nlog.Error("HTTP GET {0}", url);
				this.m_nlog.Error("HTTP Response {0}", end);
				this.m_nlog.ErrorException(exception1.GetType().ToString(), exception1);
				throw;
			}
			return statusCode;
		}
	}
}