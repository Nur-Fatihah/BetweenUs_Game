using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace Crony { public sealed class ChatMessagePointer : MonoBehaviour, IPointerClickHandler { public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Right)GetComponent<Button>().onClick.Invoke();} } }
