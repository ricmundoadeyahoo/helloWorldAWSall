# Fase 1 — Hello World en AWS (backend)
# testing

## Estructura
```
.
├── src/HelloApi/
│   ├── Function.cs        # Handler: recibe el request, guarda en DynamoDB, responde
│   └── HelloApi.csproj
├── template.yaml           # Infraestructura (SAM): API Gateway + Lambda + DynamoDB
└── .github/workflows/deploy.yml
```

## Flujo
```
Cliente HTTP → API Gateway (HTTP API) → Lambda (C#) → DynamoDB (guarda visita)
                                                     ↳ responde saludo + contador
```

`GET /hello?name=Ricardo` → `{ "message": "Hola, Ricardo!", "visitId": "...", "visitCount": 7 }`

## ⚠️ Ajustes previos necesarios al rol IAM (importante)

Este proyecto reutiliza el mismo rol de GitHub Actions (`github-actions-deploy-lambda-sqs-sns`)
que ya configuraste para el proyecto anterior, en la cuenta `114019965815`. Pero ese rol
tiene dos restricciones que hay que ampliar antes de desplegar:

### 1. La Trust Policy solo permite al repo `CloudNativeTest`

Si este Hello World vive en un **repositorio nuevo** de GitHub, hay que agregar ese repo
al `sub` de la Trust Policy (puedes tener varios repos autorizados a la vez). En CloudShell:

```bash
aws iam get-role --role-name github-actions-deploy-lambda-sqs-sns --query 'Role.AssumeRolePolicyDocument'
```

Edita el `StringLike` para incluir ambos repos, por ejemplo:
```json
"StringLike": {
  "token.actions.githubusercontent.com:sub": [
    "repo:ricmundoadeyahoo@334009166/CloudNativeTest@1389601929:*",
    "repo:ricmundoadeyahoo@334009166/NOMBRE_DEL_NUEVO_REPO@ID_DEL_REPO:*"
  ]
}
```
*(Obtén el `ID_DEL_REPO` nuevo con el mismo truco de antes: agrega temporalmente el step de
debug del token OIDC al `deploy.yml`, corre el pipeline una vez, y lee el campo `repository_id`.)*

Guarda el JSON actualizado y aplícalo con:
```bash
aws iam update-assume-role-policy --role-name github-actions-deploy-lambda-sqs-sns --policy-document file://trust-policy.json
```

### 2. Los permisos del rol no incluyen DynamoDB ni API Gateway

Agrega este bloque a la política de permisos existente (`github-actions-deploy-lambda-sqs-sns-policy`):

```json
{
  "Sid": "DynamoDbAccess",
  "Effect": "Allow",
  "Action": [
    "dynamodb:CreateTable",
    "dynamodb:DeleteTable",
    "dynamodb:DescribeTable",
    "dynamodb:TagResource"
  ],
  "Resource": "*"
},
{
  "Sid": "ApiGatewayAccess",
  "Effect": "Allow",
  "Action": [
    "apigateway:*"
  ],
  "Resource": "*"
}
```

*(El `apigateway:*` es intencionalmente amplio para simplificar el aprendizaje — SAM usa
muchas llamadas distintas de API Gateway internamente. Se puede acotar más adelante.)*

## Configuración del secret en GitHub (si es un repo nuevo)

Igual que en el proyecto anterior: Settings → Secrets and variables → Actions →
`AWS_DEPLOY_ROLE_ARN` con el mismo ARN del rol (`arn:aws:iam::114019965815:role/github-actions-deploy-lambda-sqs-sns`).

## Probar una vez desplegado

```bash
curl "$(aws cloudformation describe-stacks --stack-name hello-world-aws-stack-dev --region us-east-1 --query 'Stacks[0].Outputs[?OutputKey==\`ApiUrl\`].OutputValue' --output text)?name=Ricardo"
```

## Siguiente fase

Fase 2: un frontend en Blazor WebAssembly, hosteado en S3 + CloudFront, que consuma este
endpoint desde el navegador.
