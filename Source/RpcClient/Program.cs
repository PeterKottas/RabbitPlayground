using Commons.DTOs;
using Commons.Requests;
using Commons.Responses;
using NativeBus;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RpcClient
{
    public class AnalyticsDTO
    {
        public long ElapsedTime { get; set; }
        public int NumberOfUsers { get; set; }
        public int NumberOfThreads { get; set; }
    }

    public class DefaultTestRequestDTO
    {
        public int StartThreads = 1;
        public int MaxThreads = 5;
        public int ThreadsStep = 1;

        public int StartUsers = 1;
        public int MaxUsers = 100;
        public int UsersStep = 20;

        public int TestRuns = 100;
    }

    class Program
    {
        static void Main(string[] args)
        {
            DefaultTest(new DefaultTestRequestDTO()
                {
                    StartThreads = 1,
                    MaxThreads = 50,
                    ThreadsStep = 1,
                    StartUsers = 5,
                    MaxUsers = 6,
                    UsersStep = 4,
                    TestRuns = 500
                });
        }

        public static void AttemptToWriteCsv(string path, string content, bool open = true)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, content);
            }
            else
            {
                var prototype = string.Join(".{0}.", path.Split('.'));
                var counter = 1;
                while (true)
                {
                    path = string.Format(prototype, counter);
                    if (!File.Exists(path))
                    {
                        File.WriteAllText(path, content);
                        break;
                    }
                    counter++;
                }
            }
            if(open)
            {
                Process myProcess = new Process();
                myProcess.StartInfo.FileName = path; //not the full application path
                myProcess.Start();
            }
        }

        public static void DefaultTest(DefaultTestRequestDTO req)
        {
            var timesOuter = new BlockingCollection<BlockingCollection<AnalyticsDTO>>();


            var totalTimes = new BlockingCollection<AnalyticsDTO>();

            for (int numOfThreads = req.StartThreads; numOfThreads < req.MaxThreads; numOfThreads += req.ThreadsStep)
            {

                var watchTotal = new Stopwatch();
                var native = new Bus(new LimitedConcurrencyLevelTaskScheduler(numOfThreads));
                native.Connect(new BusConnectDTO()
                {
                    Hostname = "rabbitmq.local",
                    Password = "betfred",
                    Port = 5672,
                    UserName = "betfred"
                });

                Process myProcess = new Process();
                myProcess.StartInfo.FileName = Path.Combine(Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location) ,"../../../RpcServer/bin/Debug/RpcServer.exe"); //not the full application path
                myProcess.StartInfo.Arguments = string.Format("-t:{0}",numOfThreads);
                myProcess.Start();
                myProcess.PriorityClass = ProcessPriorityClass.High;

                var times = new BlockingCollection<AnalyticsDTO>();
                for (int usersCount = req.StartUsers; usersCount < req.MaxUsers; usersCount += req.UsersStep)
                {
                    Console.WriteLine(string.Format("{0} threads for {1} users", numOfThreads, usersCount));
                    var tasks = new List<Task>();
                    var TaskFactory = new TaskFactory(new LimitedConcurrencyLevelTaskScheduler2(usersCount));
                    Console.WriteLine("Warm-up start");
                    for (int i = 0; i < req.TestRuns/10; i++)
                    {
                        tasks.Add(TaskFactory.StartNew(() =>
                        {
                            try
                            {
                                native.Request<SampleRequestDTO, SampleResponseDTO>(new SampleRequestDTO()
                                {
                                    Name = "Peter"
                                });
                            }
                            catch (Exception)
                            {
                                throw;
                            }
                        }));
                    }
                    Task.WaitAll(tasks.ToArray());
                    tasks.Clear();
                    Console.WriteLine("Warm-up finished");
                    watchTotal.Start();
                    var watchPart = Stopwatch.StartNew();
                    for (int i = 0; i < req.TestRuns; i++)
                    {
                        tasks.Add(TaskFactory.StartNew(() =>
                        {
                            try
                            {
                                var watch = new Stopwatch();
                                watch.Start();
                                native.Request<SampleRequestDTO, SampleResponseDTO>(new SampleRequestDTO()
                                {
                                    Name = "Peter"
                                });
                                watch.Stop();
                                times.Add(new AnalyticsDTO() { ElapsedTime = watch.ElapsedMilliseconds, NumberOfUsers = usersCount, NumberOfThreads = numOfThreads });
                                //Console.WriteLine("Exit {0}", Thread.CurrentThread.ManagedThreadId);
                            }
                            catch (Exception)
                            {
                                throw;
                            }
                        }));
                    }
                    Task.WaitAll(tasks.ToArray());
                    watchTotal.Stop();
                    watchPart.Stop();
                    Console.WriteLine(string.Format("{0} threads for {1} users Took {2}", numOfThreads, usersCount, watchPart.ElapsedMilliseconds));
                }
                timesOuter.Add(times);
                totalTimes.Add(new AnalyticsDTO()
                {
                    ElapsedTime = watchTotal.ElapsedMilliseconds,
                    NumberOfThreads = numOfThreads
                });
                Console.WriteLine(string.Format("{0} threads took {1}", numOfThreads, watchTotal.ElapsedMilliseconds));
                native.DisConnect();
                myProcess.Kill();
            }
            var sb = new StringBuilder();
            var arr = timesOuter.Select(s => s.ToArray()).ToArray();
            var totalTimesArr = totalTimes.ToArray();

            sb.Append("Users count,");
            for (int numOfThreads = req.StartThreads; numOfThreads < req.MaxThreads; numOfThreads += req.ThreadsStep)
            {
                sb.Append(string.Format("{0} threads,", numOfThreads));
            }
            sb.Append("Total times");
            sb.AppendLine();
            for (int i = 0; i < arr[0].Length; i++)
            {
                sb.Append(arr[0][i].NumberOfUsers).Append(",");
                for (int j = 0; j < arr.Length; j++)
                {
                    sb.Append(arr[j][i].ElapsedTime).Append(",");
                }
                if (i < totalTimesArr.Length)
                {
                    sb.Append(totalTimesArr[i].ElapsedTime);
                }
                sb.AppendLine();
            }

            AttemptToWriteCsv("DefaultTest-output.csv", sb.ToString());
        }
    }
}
