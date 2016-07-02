using Commons.DTOs;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NativeBus
{
    public class Bus
    {
        private ConnectionFactory factory;

        private IConnection connection;

        private bool isConnected;

        TaskFactory TaskFactory;

        const string RpcQueueExchangeName = "rabbit-playground-rpc";

        public Bus(LimitedConcurrencyLevelTaskScheduler lclts)
        {
            //this.logger = logger;
            this.TaskFactory = new TaskFactory(lclts);
        }

        public bool IsConnected
        {
            get { return isConnected; }
        }

        public void Publish<T>(T message) where T : class
        {
            throw new NotImplementedException();
        }

        public TResponse Request<TRequest, TResponse>(TRequest request)
            where TRequest : class
            where TResponse : class
        {
            var queueName = string.Format("rabbit-playground-{0}-{1}", typeof(TRequest).FullName, typeof(TResponse).FullName);
            var requestReplyQueueNameInner = string.Format("rabbit-playground-response-thread-{0}-{1}-{2}", Thread.CurrentThread.ManagedThreadId, typeof(TRequest).FullName, typeof(TResponse).FullName);


            var requestChannelInner = connection.CreateModel();

            var corrId = Guid.NewGuid().ToString();
            var props = requestChannelInner.CreateBasicProperties();
            requestChannelInner.QueueDeclare(queue: requestReplyQueueNameInner,
                                            durable: false,
                                            exclusive: false,
                                            autoDelete: true,
                                            arguments: null);
            requestChannelInner.QueueBind(requestReplyQueueNameInner, RpcQueueExchangeName, requestReplyQueueNameInner);
            props.ReplyTo = requestReplyQueueNameInner;
            props.CorrelationId = corrId;

            var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));

            var requestConsumerInner = new EventingBasicConsumer(requestChannelInner);
            requestChannelInner.BasicConsume(queue: requestReplyQueueNameInner,
                     noAck: true,
                     consumer: requestConsumerInner);
            requestChannelInner.BasicPublish(exchange: RpcQueueExchangeName,
                                 routingKey: queueName,
                                 basicProperties: props,
                                 body: messageBytes);
            TResponse response = null;
            requestConsumerInner.Received += (ch, ea) =>
                {
                    System.Diagnostics.Debug.WriteLine("Exited");
                    if (ea.BasicProperties.CorrelationId == corrId)
                    {
                        response = JsonConvert.DeserializeObject<TResponse>(Encoding.UTF8.GetString(ea.Body));
                    }
                    else
                    {
                        Console.WriteLine("Wrong message expected {0} got {1} thread {2}", corrId, ea.BasicProperties.CorrelationId, Thread.CurrentThread.ManagedThreadId);
                    }
                };
            SpinWait.SpinUntil(() => response != null);
            requestChannelInner.Close();
            requestChannelInner.Dispose();
            return response;
        }

        public void Respond<TRequest, TResponse>(Func<TRequest, TResponse> responder)
            where TRequest : class
            where TResponse : class
        {
            var queueName = string.Format("rabbit-playground-{0}-{1}", typeof(TRequest).FullName, typeof(TResponse).FullName);
            TaskFactory.StartNew(() =>
            {

                var channel = connection.CreateModel();
                channel.QueueDeclare(queue: queueName,
                                        durable: true,
                                        exclusive: false,
                                        autoDelete: false,
                                        arguments: null);
                channel.QueueBind(queueName, RpcQueueExchangeName, queueName);
                //channel.BasicQos(0, 1, false);
                var consumer = new EventingBasicConsumer(channel);

                Console.WriteLine(" [x] Awaiting RPC requests");
                var svDict = new Dictionary<string, Stopwatch>();

                consumer.Received += (ch, ea) =>
                {
                    svDict.Add(ea.BasicProperties.CorrelationId, Stopwatch.StartNew());
                    TaskFactory.StartNew(() =>
                    {
                        Console.WriteLine("Enter action Thread {0}", Thread.CurrentThread.ManagedThreadId);
                        TResponse response = null;
                        var body = ea.Body;
                        var props = ea.BasicProperties;
                        var channelInner = connection.CreateModel();
                        var replyProps = channelInner.CreateBasicProperties();
                        replyProps.CorrelationId = props.CorrelationId;

                        try
                        {
                            var message = Encoding.UTF8.GetString(body);
                            var request = JsonConvert.DeserializeObject<TRequest>(message);
                            response = responder(request);
                            //response = Activator.CreateInstance<TResponse>();
                        }
                        catch (Exception e)
                        {
                            response = null;
                        }
                        finally
                        {
                            try
                            {
                                var responseText = JsonConvert.SerializeObject(response);
                                var responseBytes = Encoding.UTF8.GetBytes(responseText);
                                channelInner.BasicPublish(exchange: RpcQueueExchangeName,
                                                        routingKey: props.ReplyTo,
                                                        basicProperties: replyProps,
                                                        body: responseBytes);

                                channel.BasicAck(deliveryTag: ea.DeliveryTag,
                                                multiple: false);

                                channelInner.Close();
                                channelInner.Dispose();
                            }
                            catch (Exception)
                            {

                                throw;
                            }

                        }
                        var sw = svDict[ea.BasicProperties.CorrelationId];
                        sw.Stop();
                        Console.WriteLine("Exit action {0} \nThread {1}", sw.ElapsedMilliseconds, Thread.CurrentThread.ManagedThreadId);
                    });
                };
                var consumerTag = channel.BasicConsume(queue: queueName,
                                        noAck: false,
                                        consumer: consumer);

            });
        }

        public void Connect(BusConnectDTO request)
        {
            factory = new ConnectionFactory()
            {
                HostName = request.Hostname,
                UserName = request.UserName,
                Password = request.Password,
                Port = request.Port
            };
            try
            {
                connection = factory.CreateConnection();
                var model = connection.CreateModel();
                model.ExchangeDeclare(RpcQueueExchangeName, ExchangeType.Direct, true);
                isConnected = true;

                /*int workerThreads, complete;
                ThreadPool.GetMinThreads(out workerThreads, out complete);

                ThreadPool.SetMinThreads(200, 200);

                ThreadPool.GetMaxThreads(out workerThreads, out complete);

                ThreadPool.SetMaxThreads(2000, 2000);*/

                /*requestChannel = connection.CreateModel();
                requestReplyQueueName = requestChannel.QueueDeclare().QueueName;
                requestChannel.QueueBind(requestReplyQueueName, "easy_net_q_rpc", requestReplyQueueName);*/
            }
            catch (BrokerUnreachableException)
            {
                isConnected = false;
                throw;
            }
        }

        public void DisConnect()
        {
            connection.Close();
            isConnected = false;
        }
    }
}
