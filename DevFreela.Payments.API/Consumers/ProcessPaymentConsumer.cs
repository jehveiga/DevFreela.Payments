
using DevFreela.Payments.API.Models;
using DevFreela.Payments.API.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace DevFreela.Payments.API.Consumers
{
    public class ProcessPaymentConsumer : BackgroundService
    {
        private const string QUEUE_NAME = "Payments";
        private const string PYAMENT_APPROVED_QUEUE = "PaymentsApproved";
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly IServiceProvider _serviceProvider;

        public ProcessPaymentConsumer(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            var factory = new ConnectionFactory
            {
                HostName = "localhost"
            };

            // Criando o Canal de comunicação do RabbitMQ
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Fila responsável pelo recebimento de dados do pagamento que será efetuado
            _channel.QueueDeclare(
                queue: QUEUE_NAME,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            // Fila responsável pelo processamento das mensagens quando o apagamento foi aprovado que será repassado a aplicação que chamou o micro-service
            _channel.QueueDeclare(
                queue: PYAMENT_APPROVED_QUEUE,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null);

        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Responsável por permitir a definição de evento de mensagem
            var consumer = new EventingBasicConsumer(_channel);

            // Definição do consumo da mensagem através do evento Received
            consumer.Received += (sender, eventArgs) =>
            {
                // Obtendo o Array de bytes da mensagem na fila
                var byteArray = eventArgs.Body.ToArray();
                // Convertendo os bytes recebidos em uma string Json
                var paymentInfoJson = Encoding.UTF8.GetString(byteArray);
                // Convertendo a string Json para um objeto complexo
                var paymentInfo = JsonSerializer.Deserialize<PaymentInfoInputModel>(paymentInfoJson) ?? new PaymentInfoInputModel();

                ProcessPayment(paymentInfo);

                // Objeto que será usado para envio dos dados do projecto que foi aprovado o pagamento
                var paymentApproved = new PaymentApprovedIntegrationEvent(paymentInfo.IdProject);
                // Serialização do objeto para Json
                var paymentApprovedJson = JsonSerializer.Serialize(paymentApproved);
                // Serialização do Json para Array de bytes
                var paymentApprovedBytes = Encoding.UTF8.GetBytes(paymentApprovedJson);

                // Método que será utilizado para publicar o envio de mensagem de aprovação de pagamento para fila PYAMENT_APPROVED_QUEUE criada
                _channel.BasicPublish(
                    exchange: "",
                    routingKey: PYAMENT_APPROVED_QUEUE,
                    basicProperties: null,
                    body: paymentApprovedBytes);


                // Informando ao Message Broker que a mensagem foi recebida, marcando como lida
                _channel.BasicAck(eventArgs.DeliveryTag, false);
            };

            // Método que inicia o consumo da mensagem informando a fila que será escutada
            _channel.BasicConsume(QUEUE_NAME, false, consumer);

            return Task.CompletedTask;
        }

        private void ProcessPayment(PaymentInfoInputModel paymentInfo)
        {
            // Como ProcessPaymentConsumer está sendo chamando indefinidamente rodando em background tem que ser solicitado o serviço da forma abaixo
            using (var scope = _serviceProvider.CreateScope())
            {
                // Solicitando de serviço IPaymentService para o container de serviço da aplicação
                var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

                // Método para processar o pagamento com as informações recebidas da PaymentInfoInputModel no microservice de Payment
                paymentService.Process(paymentInfo);
            }
        }
    }
}
