/*  Author:      Camilo Piedrahita Hernández.
*   Modified on: 2016-09-20
*/


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gurux.Common;
using Gurux.Serial;
using Gurux.DLMS;
using Gurux.DLMS.ManufacturerSettings;
using System.IO;
using System.Xml.Serialization;
using Gurux.DLMS.Objects;
using Gurux.Net;
using Gurux.DLMS.Enums;
using System.Threading;
using Gurux.DLMS.Secure;

namespace MegaReader
{
	class Program
	{
		static void Trace(TextWriter writer, string text)
		{
			writer.Write(text);
			Console.Write(text);
		}

		static void TraceLine(TextWriter writer, string text)
		{
			writer.WriteLine(text);
			Console.WriteLine(text);
		}

		static void Main(string[] args)
		{
			IGXMedia media = null;
			GXCommunicatation comm = null;
			try
			{
				TextWriter logFile = new StreamWriter(File.Open("LogFile.txt", FileMode.Create));
				////////////////////////////////////////
				//Handle command line parameters.
				String id = "", host = "", port = "", pw = "", ethPort = "5000", comPort = "COM3", borderCode = "Frt0000", borderIsBackup = "P";
				String pLogicalName = "1.1.1.29.1.255", qLogicalName = "1.1.3.29.1.255";
				ushort pShortName = 7216, qShortName = 7368;
				bool trace = false, iec = true;
				bool ethernet = true;
				Authentication auth = Authentication.None;
				ushort shortName = 25200;
				var logicalName = "1.0.99.1.0.255";

				int pCol = 0;
				int qCol = 0;
				double factor = 0.1;

				id = "lgz";
				host = "186.86.153.16";
				ethPort = "5000";
				comPort = "COM3";
				pw = "00000000";
				auth = Authentication.Low;
				iec = false;
				trace = false;

				if (ethernet)// TCP/IP Port
				{
					media = new Gurux.Net.GXNet();
					port = ethPort;
				}
				else// Serial Port
				{
					port = comPort;
					media = new GXSerial();
				}
				////////////////////////////////////////
				//Initialize connection settings.
				if (media is GXSerial)
				{
					GXSerial serial = media as GXSerial;
					serial.PortName = port;
					if (iec)
					{
						serial.BaudRate = 300;
						serial.DataBits = 7;
						serial.Parity = System.IO.Ports.Parity.Even;
						serial.StopBits = System.IO.Ports.StopBits.One;
					}
					else
					{
						serial.BaudRate = 9600;
						serial.DataBits = 8;
						serial.Parity = System.IO.Ports.Parity.None;
						serial.StopBits = System.IO.Ports.StopBits.One;
					}
				}
				else if (media is GXNet)
				{
					Gurux.Net.GXNet net = media as GXNet;
					net.Port = Convert.ToInt32(port);
					net.HostName = host;
					net.Protocol = Gurux.Net.NetworkType.Tcp;
				}
				else
				{
					throw new Exception("Unknown media type.");
				}
				if (id == null)
				{
					throw new Exception("Unknown manufacturer: " + id);
				}
				GXDLMSSecureClient dlms = new GXDLMSSecureClient();
				//Update Obis code list so we can get right descriptions to the objects.
				comm = new GXCommunicatation(dlms, media, iec, auth, pw);
				comm.Trace = trace;
				comm.InitializeConnection(id);
				GXDLMSObjectCollection objects = null;
				string path = host.Replace('.', '_') + "_" + port.ToString() + ".xml";

				List<Type> extraTypes = new List<Type>(Gurux.DLMS.GXDLMSClient.GetObjectTypes());
				extraTypes.Add(typeof(GXDLMSAttributeSettings));
				extraTypes.Add(typeof(GXDLMSAttribute));
				XmlSerializer x = new XmlSerializer(typeof(GXDLMSObjectCollection), extraTypes.ToArray());
				//You can save association view, but make sure that it is not change.
				//Save Association view to the cache so it is not needed to retrieve every time.

				if (File.Exists(path))
				{
					try
					{
						using (Stream stream = File.Open(path, FileMode.Open))
						{
							TraceLine(logFile, "Get available objects from the cache.");
							//Console.WriteLine("Get available objects from the cache.");
							objects = x.Deserialize(stream) as GXDLMSObjectCollection;
							stream.Close();
						}
					}
					catch (Exception ex)
					{
						if (File.Exists(path))
						{
							File.Delete(path);
						}
						throw ex;
					}
				}
				else
				{
					TraceLine(logFile, "Get available objects from the device.");
					//Console.WriteLine("Get available objects from the device.");
					objects = comm.GetAssociationView();
					GXDLMSObjectCollection objs = objects.GetObjects(new ObjectType[] { ObjectType.Register, ObjectType.ExtendedRegister, ObjectType.DemandRegister });
					try
					{
						using (Stream stream = File.Open(path, FileMode.Create))
						{
							TextWriter writer = new StreamWriter(stream);
							x.Serialize(writer, objects);
							writer.Close();
							stream.Close();
						}
					}
					catch (Exception ex)
					{
						if (File.Exists(path))
						{
							File.Delete(path);
						}
						throw ex;
					}
				}
				//Find profile generics and read them.      
				if (objects != null)
				{
					GXDLMSObject it = objects.GetObjects(ObjectType.ProfileGeneric).First(i => i.ShortName == shortName | i.LogicalName == logicalName);
					try
					{
						TraceLine(logFile, "" + it.Name);
						//Console.WriteLine(it.Name);
						comm.Read(it, 3);
						GXDLMSObject[] cols = (it as GXDLMSProfileGeneric).GetCaptureObject();

						// Read Columns
						for (int i = 0; i < cols.Length; i++)
						{
							if (cols[i].LogicalName == pLogicalName | cols[i].ShortName == pShortName) pCol = i;
							if (cols[i].LogicalName == qLogicalName | cols[i].ShortName == qShortName) qCol = i;
						}

						TraceLine(logFile, "Profile Generic " + it.Name + " Columns:");
						StringBuilder sb = new StringBuilder();
						bool First = true;
						foreach (GXDLMSObject col in cols)
						{
							if (!First)
							{
								sb.Append(" | ");
							}
							First = false;
							sb.Append(col.Name);
							sb.Append(" ");
							sb.Append(col.Description);
						}
						TraceLine(logFile, sb.ToString());
					}
					catch (Exception ex)
					{
						TraceLine(logFile, "Err! Failed to read columns:" + ex.Message);
						//Continue reading.
					}
					TraceLine(logFile, "Reading " + it.GetType().Name + " " + it.Name + " " + it.Description);
					long entriesInUse = Convert.ToInt64(comm.Read(it, 7));
					long entries = Convert.ToInt64(comm.Read(it, 8));
					TraceLine(logFile, "Entries: " + entriesInUse + "/" + entries);
					//If there are no columns or rows.
					if (entriesInUse == 0 || (it as GXDLMSProfileGeneric).CaptureObjects.Count == 0)
					{
						//continue;
					}
					try
					{
						//Read last day from Profile Generic.    
						var previosDay = DateTime.Now.AddDays(-1);
						var fromDate = new DateTime(previosDay.Year, previosDay.Month, previosDay.Day, 1, 0, 0);
						var nextDay = previosDay.AddDays(1);
						var toDate = new DateTime(nextDay.Year, nextDay.Month, nextDay.Day, 0, 0, 0);

						object[] rows = comm.ReadRowsByRange(it as GXDLMSProfileGeneric, fromDate, toDate);
						StringBuilder sb = new StringBuilder();
						sb.Append("\r\n");
						sb.Append("    Clock    |  P  |  Q  |"); // Headers
						sb.Append("\r\n");
						var hour = 0;
						var readings = new string[24];
						var sbAsic = new StringBuilder();
						var totalP = 0.0;
						foreach (object[] row in rows)
						{
							//Clock
							var clock = row[0];
							sb.Append(Convert.ToDateTime(clock));
							sb.Append(" | ");

							//Active Power
							var p = row[pCol];
							sb.Append(Convert.ToDouble(p));
							sb.Append(" | ");

							//Reactive Power
							var q = row[qCol];
							sb.Append(Convert.ToDouble(q));
							sb.Append(" | ");
							sb.Append("\r\n");
							// Cereate ASIC file content
							if (hour < 24)
							{
								var value = (Convert.ToDouble(p) * factor).ToString("#.#");
								readings[hour] = (value == "") ? "0" : value;
								//totalP += Convert.ToDouble(p);
							}
							hour++;
						}
						Trace(logFile, sb.ToString());
						// Procesar lecturas recibidas desde el medidor
						sbAsic.Append(borderCode + ";" + borderIsBackup + ";" + totalP);
						for (int i = 0; i < readings.Length; i++)
						{
							sbAsic.Append(";" + readings[i]);

						}
						File.WriteAllText(@"C:\Users\RightSide\Documents\Readings\" + DateTime.Now.ToString("yyyy") + @"\" + DateTime.Now.ToString("MM") + @"\" + DateTime.Now.ToString("yyyy-MM-dd") + " - " + borderCode + borderIsBackup + ".txt", sbAsic.ToString());
					}
					catch (Exception ex)
					{
						TraceLine(logFile, "Error! Failed to read last day: " + ex.Message);
						//Continue reading.
					}
				}
				TraceLine(logFile, "\r\n");
				TraceLine(logFile, "Finish reading meter");
				logFile.Flush();
				logFile.Close();
			}
			catch (Exception ex)
			{
				if (comm != null)
				{
					comm.Close();
				}
				Console.WriteLine(ex.Message);
				if (!System.Diagnostics.Debugger.IsAttached)
				{
					//Console.ReadKey();
				}
			}
			finally
			{
				if (comm != null)
				{
					comm.Close();
				}
				if (System.Diagnostics.Debugger.IsAttached)
				{
					//Console.WriteLine("Ended. Press any key to continue.");
					//Console.ReadKey();
				}
			}
		}
	}
}
