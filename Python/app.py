from flask import Flask, request, jsonify, render_template
from azure.identity import ClientSecretCredential
from azure.keyvault.secrets import SecretClient
from azure.storage.blob import BlobServiceClient, BlobClient, ContainerClient
from azure.ai.vision.imageanalysis import ImageAnalysisClient
from azure.ai.vision.imageanalysis.models import VisualFeatures
from dotenv import load_dotenv, dotenv_values
from azure.core.credentials import AzureKeyCredential
import azure.cognitiveservices.speech as speechsdk
import openai
import logging
import json
import base64
import os
from azure.keyvault.secrets import SecretClient
from azure.identity import DefaultAzureCredential
from openai import AzureOpenAI

app = Flask(__name__)

load_dotenv()


keyVaultUri = os.environ.get('AKV_URL')
tenant_id = os.environ.get('SP_TENANT_ID')
client_id = os.environ.get('SP_CLIENT_ID')
client_secret = os.environ.get('SP_CLIENT_SECRET')
storage_url = os.environ.get('STORAGE_URL')
container = os.environ.get('STORAGE_CONTAINER')


SECRET_AOAI_KEY = "AOAI-KEY"
SECRET_COGNITIVE_KEY = "COGNITIVE-KEY"

credential = ClientSecretCredential(tenant_id=tenant_id, client_id=client_id, client_secret=client_secret)


blob_service_client = BlobServiceClient(
        account_url=storage_url,
        credential=credential)

container_client = blob_service_client.get_container_client(container)

def authenticate_vision_client():

    client = SecretClient(vault_url=keyVaultUri, credential=credential)

    # Hackathon step 2: Retrieve Cognitive API keys from Key Vault
    key = client.get_secret(SECRET_COGNITIVE_KEY).value
    
    endpoint = os.environ.get('COGNITIVE_URL')
    print(key)
    print(endpoint)
    vi_credential=AzureKeyCredential(key)
    vision_client = ImageAnalysisClient(endpoint=endpoint, credential=vi_credential)

    return vision_client

def openai(text):
    client=SecretClient(vault_url=keyVaultUri, credential=credential)
    
    # Hackathon step 2: Retrieve AOAI keys from Key Vault
    key=client.get_secret(SECRET_AOAI_KEY).value

    endpoint=os.environ.get('AOAI_URL')
    deployment=os.environ.get('AOAI_DEPLOYMENT')
    client = AzureOpenAI(
        azure_endpoint = endpoint, 
        api_key=key,  
        api_version="2024-02-01"
        )
    
    response = client.chat.completions.create(
    model=deployment,
    messages=[
        {"role": "system", "content": "You are a helpful assistant."},
        {"role": "user", "content": text}
    ]
    )
    
    response_from_aoai=response.choices[0].message.content

    return response_from_aoai

# generate the text to speech for the text returned from azure openai
def text_to_speech(text):
    client = SecretClient(vault_url=keyVaultUri, credential=credential)
    key = client.get_secret(SECRET_COGNITIVE_KEY).value
    region = os.environ.get('COGNITIVE_REGION')


    #define speech config and audio config for the speech synthesis object
    speech_config =speechsdk.SpeechConfig(subscription=key, region=region)

    speech_config.speech_synthesis_voice_name = "en-SG-LunaNeural"

    #create the speech synthesizer object
    synthesizer = speechsdk.SpeechSynthesizer(speech_config=speech_config)

    result = synthesizer.speak_text_async(text).get()
    
    # Check result
    if result.reason == speechsdk.ResultReason.SynthesizingAudioCompleted:
        print("Speech synthesized for text [{}]".format(text))
    elif result.reason == speechsdk.ResultReason.Canceled:
        cancellation_details = result.cancellation_details
        print("Speech synthesis canceled: {}".format(cancellation_details.reason))
        if cancellation_details.reason == speechsdk.CancellationReason.Error:
            print("Error details: {}".format(cancellation_details.error_details))

    return "Speech synthesis completed"


# Azure Image Analysis client library for Python - version 1.0.0b3
# https://learn.microsoft.com/en-us/python/api/overview/azure/ai-vision-imageanalysis-readme?view=azure-python-preview


title ='Hunter Y'

@app.route('/', methods=['GET', 'POST'])
def aivision():
        images =[]
        analysis_results = ""
        if request.method == 'GET':
            
            
            blob_list = container_client.list_blobs()
            for blob in blob_list:
                print(blob.name)
                blob_client = container_client.get_blob_client(blob.name)
                download_stream = blob_client.download_blob()
                image_data = download_stream.readall()
                # Encode image data to base64
                encoded_image = base64.b64encode(image_data).decode('utf-8')
                images.append(encoded_image)
        
                print(f"Images count: {len(images)}")

            return render_template('index.html', images=images, analysis_results=None, title=title)
            
        if request.method == 'POST':
             analysis_results = None 

             selected_image_base64 = request.form.get('selected_image')
             print("Selected image received")
             print(selected_image_base64[:100])  # Print the first 100 characters for verification
             if selected_image_base64:
                client = authenticate_vision_client()
                image_data = base64.b64decode(selected_image_base64)


                # Hackathon step 3: Extract text from the selected image
                result = client.analyze(image_data, visual_features=[VisualFeatures.READ])
                print(result)

                # Extract text from the result
                combined_text = ""
                if result.read is not None:
                    words = []
                    for line in result.read.blocks[0].lines:
                        print(f"   Line: '{line.text}', Bounding box {line.bounding_polygon}")
                        for word in line.words:
                            print(f"     Word: '{word.text}")
                            words.append(word.text)
                    combined_text = " ".join(words) 
                    print(combined_text)
                    
                    read_results = combined_text  

                    # Hackathon step 4: Analyze the text and find the hidden info
                    final_result=openai(read_results)

                    analysis_results = final_result

                    #Hackathon step 5: Convert the result to speech
                    #Use the same cognitive service for speech

                    text_to_speech(analysis_results)
                            
                return render_template('index.html', analysis_results=analysis_results, images=selected_image_base64)
            

if __name__ == '__main__':
    app.run(debug=True)